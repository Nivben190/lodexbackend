using Looxdex.Api.Configuration;
using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Detection;

/// <summary>
/// Runs fashion detection over newly ingested posts, a small batch at a time.
///
/// Detection happens here rather than on a user request, so an image is analysed
/// exactly once no matter how often it is viewed. That is what keeps inference
/// inside the free tier as traffic grows.
/// </summary>
public class DetectionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OnnxDetectionOptions _options;
    private readonly GarmentCutoutOptions _cutoutOptions;
    private readonly ILogger<DetectionWorker> _logger;

    /// <summary>
    /// One sweep at a time. The timer and the admin endpoint both call in, and two
    /// sweeps overlapping each pick up the same pending post and each write a full
    /// set of items — which is how a look ends up listing its trousers four times.
    /// </summary>
    private readonly SemaphoreSlim _sweepGate = new(1, 1);

    public DetectionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OnnxDetectionOptions> options,
        IOptions<GarmentCutoutOptions> cutoutOptions,
        ILogger<DetectionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _cutoutOptions = cutoutOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Detection worker idle: ONNX detection is disabled.");
            return;
        }

        // Let the app finish starting before competing for the connection pool.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await RunSweepAsync(stoppingToken);

                // Nothing pending: wait the full interval. Work remaining: come
                // straight back so a fresh ingest drains promptly.
                if (processed == 0)
                {
                    await Task.Delay(_options.Interval, stoppingToken);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Detection sweep failed; retrying after the interval.");
                await Task.Delay(_options.Interval, stoppingToken);
            }
        }
    }

    /// <summary>Processes one batch. Returns how many posts were attempted.</summary>
    public async Task<int> RunSweepAsync(CancellationToken ct)
    {
        await _sweepGate.WaitAsync(ct);
        try
        {
            return await SweepAsync(ct);
        }
        finally
        {
            _sweepGate.Release();
        }
    }

    private async Task<int> SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LooxdexDbContext>();
        var detector = scope.ServiceProvider.GetRequiredService<IFashionDetector>();
        var fetcher = scope.ServiceProvider.GetRequiredService<ImageFetcher>();
        var cutouts = scope.ServiceProvider.GetRequiredService<IGarmentCutoutService>();

        if (!detector.Enabled) return 0;

        var pending = await db.FeedPosts
            .Include(p => p.DetectedItems)
            .Where(p => p.DetectionState == DetectionState.Pending
                        && p.DetectionAttempts < _options.MaxAttempts)
            .OrderByDescending(p => p.Rank)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        foreach (var post in pending)
        {
            ct.ThrowIfCancellationRequested();
            post.DetectionAttempts++;

            try
            {
                var photo = await fetcher.FetchAsync(post.ImageUrl, ct);
                if (photo is null)
                {
                    if (post.DetectionAttempts >= _options.MaxAttempts)
                    {
                        post.DetectionState = DetectionState.Failed;
                    }
                    continue;
                }

                var hits = await detector.DetectAsync(photo, ct);

                if (hits.Count == 0)
                {
                    // A real image with nothing wearable in it should not be retried
                    // forever; give up once the attempt budget is spent.
                    post.DetectionState = post.DetectionAttempts >= _options.MaxAttempts
                        ? DetectionState.Skipped
                        : DetectionState.Pending;
                }
                else
                {
                    // Analysing a post again replaces what it had. Appending would
                    // leave a retried post listing everything twice.
                    if (post.DetectedItems.Count > 0)
                    {
                        db.DetectedItems.RemoveRange(post.DetectedItems);
                    }

                    // Cut each garment out of the same photo while it is in hand, so
                    // the feed can show product tiles instead of slices of the scene.
                    var pass = await cutouts.RenderAsync(photo, hits, ct);
                    var rendered = pass.Items;
                    post.HasPerson = pass.PersonPresent;
                    var claimed = new List<MaskFootprint>();

                    for (var i = 0; i < hits.Count; i++)
                    {
                        var h = hits[i];
                        rendered.TryGetValue(i, out var render);

                        // Nobody in the photo. Both models are working outside what
                        // they were trained on, so the label is not to be trusted —
                        // but the thing in the picture is real, and the shops can
                        // say what it is. Only the best guess is kept, and matching
                        // renames it from what the sellers call it.
                        if (!pass.PersonPresent
                            && h.Score < _cutoutOptions.FlatLayScore
                            && (i > 0 || h.Score < _cutoutOptions.FlatLayFloor))
                        {
                            _logger.LogInformation(
                                "Dropped {Label} at {Score:P0} on post {PostId}: no one in the photo.",
                                h.Label, h.Score, post.Id);
                            continue;
                        }

                        // Two models have to agree before a middling detection is
                        // believed. Where the segmenter knows the garment and finds
                        // none of it under the box, the detector is seeing things —
                        // bare legs read as trousers, a floorboard as a shoe — and
                        // only a confident detection survives that disagreement.
                        if (render is { Verdict: MaskVerdict.Absent }
                            && h.Score < _options.UnconfirmedScore)
                        {
                            _logger.LogInformation(
                                "Dropped {Label} at {Score:P0} on post {PostId}: no mask under the box.",
                                h.Label, h.Score, post.Id);
                            continue;
                        }

                        // One garment, two names. A leopard coat comes back as both
                        // a jacket and a buttoned shirt, and both land on precisely
                        // the same pixels — so the second one is not another item,
                        // it is the detector's second guess. Hits arrive in score
                        // order, so the one already kept is the better guess.
                        if (render?.MaskKey is { } footprint)
                        {
                            if (claimed.Any(c => c.Overlap(footprint) > 0.7))
                            {
                                _logger.LogInformation(
                                    "Dropped {Label} at {Score:P0} on post {PostId}: same garment, second guess.",
                                    h.Label, h.Score, post.Id);
                                continue;
                            }

                            claimed.Add(footprint);
                        }

                        var item = new DetectedItemEntity
                        {
                            FeedPostId = post.Id,
                            Label = h.Label,
                            LabelHe = h.LabelHe,
                            Category = h.Category,
                            Score = h.Score,
                            BoxX = h.X,
                            BoxY = h.Y,
                            BoxWidth = h.Width,
                            BoxHeight = h.Height
                        };

                        item.CutoutAttemptedAt = DateTime.UtcNow;

                        if (render?.Cutout is { } cutout)
                        {
                            item.CutoutImageId = StoreCutout(db, cutout);
                            item.ColorName = cutout.Color.Name;
                            item.ColorHex = cutout.Color.Hex;
                        }

                        db.DetectedItems.Add(item);
                    }

                    post.DetectionState = DetectionState.Completed;
                    post.DetectedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Detection failed for post {PostId}.", post.Id);
                if (post.DetectionAttempts >= _options.MaxAttempts)
                {
                    post.DetectionState = DetectionState.Failed;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        var completed = pending.Count(p => p.DetectionState == DetectionState.Completed);
        _logger.LogInformation(
            "Detection sweep: {Completed}/{Total} posts analysed.", completed, pending.Count);

        return pending.Count;
    }

    /// <summary>
    /// Renders cutouts for items that were detected before there was a cutout stage.
    ///
    /// Only the segmenter runs: the boxes are already in the database, so there is
    /// no reason to pay for detection a second time. Returns posts and items done.
    /// </summary>
    public async Task<(int Posts, int Items)> RebuildCutoutsAsync(
        int? batchSize, bool force, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LooxdexDbContext>();
        var fetcher = scope.ServiceProvider.GetRequiredService<ImageFetcher>();
        var cutouts = scope.ServiceProvider.GetRequiredService<IGarmentCutoutService>();
        var cutoutOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<GarmentCutoutOptions>>().Value;

        if (!cutouts.Enabled) return (0, 0);

        var take = Math.Clamp(batchSize ?? cutoutOptions.BackfillBatchSize, 1, 50);

        var posts = await db.FeedPosts
            .Include(p => p.DetectedItems)
            .Where(p => p.DetectedItems.Any(d => force || d.CutoutAttemptedAt == null))
            .OrderByDescending(p => p.Rank)
            .Take(take)
            .ToListAsync(ct);

        if (posts.Count == 0) return (0, 0);

        var rendered = 0;

        foreach (var post in posts)
        {
            ct.ThrowIfCancellationRequested();

            var items = post.DetectedItems
                .Where(d => force || d.CutoutAttemptedAt == null)
                .ToList();

            if (items.Count == 0) continue;

            var photo = await fetcher.FetchAsync(post.ImageUrl, ct);
            if (photo is null) continue;

            var hits = items
                .Select(d => new DetectionHit(
                    d.Label, d.LabelHe, d.Category, d.Score,
                    d.BoxX, d.BoxY, d.BoxWidth, d.BoxHeight))
                .ToList();

            var results = (await cutouts.RenderAsync(photo, hits, ct)).Items;

            for (var i = 0; i < items.Count; i++)
            {
                items[i].CutoutAttemptedAt = DateTime.UtcNow;

                if (results.GetValueOrDefault(i)?.Cutout is not { } cutout)
                {
                    // A re-run that now rejects this mask must also drop what the
                    // old rules produced, or the item keeps showing a tile the
                    // current thresholds would never have written.
                    if (force) items[i].CutoutImageId = null;
                    continue;
                }

                items[i].CutoutImageId = StoreCutout(db, cutout);
                items[i].ColorName = cutout.Color.Name;
                items[i].ColorHex = cutout.Color.Hex;
                rendered++;
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Cutout backfill: {Items} items across {Posts} posts.", rendered, posts.Count);

        return (posts.Count, rendered);
    }

    /// <summary>Stores a rendered tile alongside the uploads and returns its id.</summary>
    private static string StoreCutout(LooxdexDbContext db, GarmentCutout cutout)
    {
        var id = Guid.NewGuid().ToString("N");

        db.UploadedImages.Add(new UploadedImageEntity
        {
            Id = id,
            OwnerKey = "system:cutout",
            ContentType = cutout.ContentType,
            Data = cutout.Image,
            ByteSize = cutout.Image.Length,
            CreatedAt = DateTime.UtcNow
        });

        return id;
    }
}
