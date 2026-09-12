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
    private readonly ILogger<DetectionWorker> _logger;

    public DetectionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OnnxDetectionOptions> options,
        ILogger<DetectionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LooxdexDbContext>();
        var detector = scope.ServiceProvider.GetRequiredService<IFashionDetector>();

        if (!detector.Enabled) return 0;

        var pending = await db.FeedPosts
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
                var hits = await detector.DetectAsync(post.ImageUrl, ct);

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
                    db.DetectedItems.AddRange(hits.Select(h => new DetectedItemEntity
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
                    }));

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
}
