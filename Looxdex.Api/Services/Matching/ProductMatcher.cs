using Looxdex.Api.Configuration;
using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Services.Detection;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp.Processing;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Matching;

/// <summary>
/// Finds, for each detected garment, the shop photographs that look most like it.
///
/// This is what a cut-out cannot be. A garment worn in a street photograph is
/// creased, half-turned and partly hidden, and no amount of masking turns it into
/// a product shot — but the catalogue is full of product shots, and one of them is
/// usually the same kind of thing. So the tile stops being a scrap of the original
/// photo and becomes a picture of the garment.
/// </summary>
public class ProductMatcher
{
    private readonly LooxdexDbContext _db;
    private readonly IClipEmbedder _embedder;
    private readonly IVisualSearch _visualSearch;
    private readonly ImageFetcher _fetcher;
    private readonly IHttpContextAccessor _http;
    private readonly ProductMatchOptions _options;
    private readonly VisualSearchOptions _searchOptions;
    private readonly ILogger<ProductMatcher> _logger;

    public ProductMatcher(
        LooxdexDbContext db,
        IClipEmbedder embedder,
        IVisualSearch visualSearch,
        ImageFetcher fetcher,
        IHttpContextAccessor http,
        IOptions<ProductMatchOptions> options,
        IOptions<VisualSearchOptions> searchOptions,
        ILogger<ProductMatcher> logger)
    {
        _db = db;
        _embedder = embedder;
        _visualSearch = visualSearch;
        _fetcher = fetcher;
        _http = http;
        _options = options.Value;
        _searchOptions = searchOptions.Value;
        _logger = logger;
    }

    public bool Enabled => _visualSearch.Enabled || (_options.Enabled && _embedder.Enabled);

    /// <summary>
    /// Matches a batch of detected items that have none yet. Returns how many items
    /// were looked at and how many came away with something.
    /// </summary>
    public async Task<(int Examined, int Matched)> RunAsync(
        int batchSize, bool force, CancellationToken ct)
    {
        if (!Enabled) return (0, 0);

        var pending = await _db.DetectedItems
            .Include(d => d.Alternatives)
            .Include(d => d.FeedPost)
            .Where(d => force || d.MatchAttemptedAt == null)
            .OrderByDescending(d => d.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(ct);

        if (pending.Count == 0) return (0, 0);

        // Loaded once for the batch: the catalogue is a few thousand rows of
        // vectors, and reloading it per item would dominate the cost of the work.
        var catalogue = await LoadCatalogueAsync(ct);
        var matched = 0;

        foreach (var item in pending)
        {
            ct.ThrowIfCancellationRequested();
            item.MatchAttemptedAt = DateTime.UtcNow;

            if (item.Alternatives.Count > 0)
            {
                // Replacing, never appending: an item matched twice was listing
                // every shop twice, and the tile was showing whichever row had been
                // written first.
                _db.ShoppingAlternatives.RemoveRange(item.Alternatives);
            }

            // Google first, when we have a key for it: it answers with shops the
            // wearer can buy from at today's prices, which our own catalogue — a
            // fixed dataset with neither links nor prices — never can.
            if (_visualSearch.Enabled && await MatchOnlineAsync(item, ct))
            {
                matched++;
                continue;
            }

            var categories = ProductCategories.For(item.Label);
            if (categories.Length == 0) continue;

            var vector = await EmbedItemAsync(item, ct);
            if (vector is null) continue;

            var candidates = catalogue
                .Where(p => categories.Contains(p.Category, StringComparer.OrdinalIgnoreCase))
                .ToList();

            // Colour is known on both sides and CLIP is careless with it — a
            // leopard trouser and a pink running short sit close together in its
            // space, because both are "a pair of trousers on white". Where both
            // colours are known, they have to agree.
            if (!string.IsNullOrWhiteSpace(item.ColorName))
            {
                var sameColour = candidates
                    .Where(p => string.Equals(p.Colour, item.ColorName, StringComparison.Ordinal))
                    .ToList();

                if (sameColour.Count > 0) candidates = sameColour;
            }

            var best = candidates
                .Select(p => (Product: p, Score: ClipEmbedder.Similarity(vector, p.Embedding)))
                .Where(x => x.Score >= _options.MinSimilarity)
                .OrderByDescending(x => x.Score)
                .Take(_options.Alternatives)
                .ToList();

            if (best.Count == 0)
            {
                _logger.LogInformation(
                    "Nothing close enough for {Label} (item {Id}).", item.Label, item.Id);
                continue;
            }

            foreach (var (product, score) in best)
            {
                _db.ShoppingAlternatives.Add(new ShoppingAlternativeEntity
                {
                    DetectedItemId = item.Id,
                    Brand = product.Brand,
                    Name = product.Name,
                    Price = product.Price,
                    ImageUrl = product.ImageId,   // resolved to a URL when read
                    StoreUrl = product.StoreUrl
                });
            }

            matched++;

            _logger.LogInformation(
                "Matched {Label} to {Name} at {Score:F3}.",
                item.Label, best[0].Product.Name, best[0].Score);
        }

        await _db.SaveChangesAsync(ct);
        return (pending.Count, matched);
    }

    /// <summary>
    /// Orders a result: whether it is a catalogue photograph at all, then how much
    /// it looks like the garment. Expressed as one number so it can be sorted, with
    /// the studio verdict occupying the whole above and likeness the fraction.
    /// </summary>
    private static double Rank(double likeness, double studio) =>
        (studio >= StudioThreshold ? 1 : 0) + Math.Clamp(likeness, 0, 0.999);

    /// <summary>
    /// How blank the background has to be to count as a catalogue photograph. A
    /// leopard jacket on white scores 0.56 here; the same jacket photographed on
    /// three different people scored 0.00, 0.00 and 0.00.
    /// </summary>
    private const double StudioThreshold = 0.35;

    /// <summary>
    /// Re-orders the shop results already stored, best photograph first, without
    /// asking the search service anything. Only the thumbnails are re-read, so
    /// this is free — which matters, because getting the ordering right took
    /// several attempts and each lookup is metered.
    /// </summary>
    public async Task<(int Items, int Moved)> RestageAsync(int batchSize, CancellationToken ct)
    {
        var items = await _db.DetectedItems
            .Include(d => d.Alternatives)
            .Where(d => d.Alternatives.Any())
            .OrderBy(d => d.Id)
            .Take(Math.Clamp(batchSize, 1, 200))
            .ToListAsync(ct);

        var moved = 0;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            var vector = await EmbedItemAsync(item, ct);
            var scored = new List<(ShoppingAlternativeEntity Row, double Score)>();

            foreach (var row in item.Alternatives)
            {
                var thumbnail = await _fetcher.FetchAsync(row.ImageUrl, ct);

                if (thumbnail is null)
                {
                    scored.Add((row, -1));
                    continue;
                }

                var likeness = 0.0;

                if (vector is not null)
                {
                    var candidate = await _embedder.EmbedAsync(thumbnail, ct);
                    if (candidate is not null) likeness = ClipEmbedder.Similarity(vector, candidate);
                }

                scored.Add((row, Rank(likeness, StudioLook.Score(thumbnail))));
            }

            var order = 0;
            var first = item.Alternatives.OrderBy(a => a.Rank).ThenBy(a => a.Id).First().Id;
            var ordered = scored.OrderByDescending(x => x.Score).ToList();

            foreach (var (row, _) in ordered)
            {
                row.Rank = order++;
            }

            if (ordered[0].Row.Id != first) moved++;

            // Nothing here is a catalogue photograph — every result is somebody
            // wearing the thing. Worth asking again, where the packshots tend to
            // sit deeper in the answers. What it has is kept until something better
            // actually arrives: the matcher replaces an item's results when it finds
            // any, and throwing these away first only guarantees an empty tile if
            // the second attempt comes back with nothing.
            if (ordered[0].Score < 1) item.MatchAttemptedAt = null;
        }

        await _db.SaveChangesAsync(ct);
        return (items.Count, moved);
    }

    /// <summary>
    /// Looks the garment up in the shops. Returns whether anything was found.
    /// </summary>
    private async Task<bool> MatchOnlineAsync(DetectedItemEntity item, CancellationToken ct)
    {
        // A detection the detector half believed is usually a garment seen from
        // behind, or half of one. Searching the shops for it spends a lookup to
        // find something that looks like whatever the mask happened to keep — a
        // shirt with a bag strap across it comes back as a holster, correctly.
        //
        // Except on a flat-lay, where a low score says nothing about whether the
        // thing is there — only that the detector could not name it, which is the
        // very thing the shops are being asked.
        if (item.FeedPost is { HasPerson: true } && item.Score < _searchOptions.MinDetectionScore)
        {
            return false;
        }

        // The cut-out when there is one. For the items a cut-out can never be made
        // of — a watch on a wrist, glasses on a face, which the parser has no class
        // for — a plain crop is the only picture there is, and Lens happens to be
        // very good at precisely those. The seller's title still has to agree, so a
        // crop full of pavement returns nothing rather than nonsense.
        string? imageUrl;

        if (item.FeedPost is { HasPerson: false })
        {
            // A flat-lay is a photograph of one thing. The detection box is a guess
            // made outside the detector's training, so the whole picture is what
            // gets searched — which is also exactly what a person would do with it.
            imageUrl = item.FeedPost.ImageUrl;
        }
        else
        {
            var searchImageId = item.CutoutImageId;

            if (string.IsNullOrWhiteSpace(searchImageId) && CanSearchByCrop(item))
            {
                searchImageId = await EnsureCropAsync(item, ct);
            }

            imageUrl = PublicUrlFor(searchImageId);
        }

        if (string.IsNullOrWhiteSpace(imageUrl)) return false;

        var matches = await _visualSearch.FindAsync(imageUrl, ct);
        if (matches.Count == 0) return false;

        // In a photograph with nobody in it the detector's label is a guess made
        // outside its training. The shops have just looked at the same picture and
        // agreed on what it is, so their word replaces the guess — and it is their
        // word the rest of the matching is then checked against.
        // Only a photograph of one thing may be renamed by the shops. "No person"
        // is a weaker signal than it looks: an outfit shot cropped below the face,
        // with long sleeves and trousers, shows the parser nothing but cloth and it
        // reports an empty room. Renaming every item on such a post left one look
        // listing its shoe, jacket, belt and bag all as "trousers", each renamed by
        // its own search. A photograph with five garments in it is an outfit,
        // whoever is or is not visible in it.
        if (item.FeedPost is { HasPerson: false } && await IsSingleItemAsync(item, ct))
        {
            var named = ProductCategories.LabelFromTitles(matches.Select(m => m.Title));

            if (named is not null && !string.Equals(named, item.Label, StringComparison.OrdinalIgnoreCase))
            {
                var label = FashionpediaLabels.Resolve(named);

                if (label is not null)
                {
                    _logger.LogInformation(
                        "Post {PostId}: the shops call this a {Named}, not a {Guess}.",
                        item.FeedPostId, named, item.Label);

                    item.Label = named;
                    item.LabelHe = label.LabelHe;
                    item.Category = label.Category;
                }
            }
        }

        // Google finds what looks similar anywhere, which includes things that are
        // not clothes: the first jacket it was asked about came back as a shoulder
        // holster, because a dark strappy shape is a dark strappy shape. So its
        // candidates are checked against the garment the way the catalogue is —
        // Lens for reach, CLIP for whether it is actually the same kind of thing.
        var verified = await VerifyAsync(item, matches, ct);
        if (verified.Count == 0)
        {
            _logger.LogInformation(
                "{Label}: {Found} shop results, none of them the garment.",
                item.Label, matches.Count);
            return false;
        }

        matches = verified;

        var rank = 0;

        foreach (var match in matches)
        {
            _db.ShoppingAlternatives.Add(new ShoppingAlternativeEntity
            {
                DetectedItemId = item.Id,
                Rank = rank++,
                Brand = Trim(match.Source, 120),
                Name = Trim(match.Title, 200),
                Price = match.Price,
                Currency = Trim(match.Currency, 8),
                ImageUrl = Trim(match.ThumbnailUrl, 1024),
                StoreUrl = Trim(match.Link, 1024)
            });
        }

        _logger.LogInformation(
            "{Label} found at {Shop}: {Title}", item.Label, matches[0].Source, matches[0].Title);

        return true;
    }

    /// <summary>
    /// Keeps the shop results that actually look like the garment, best first.
    ///
    /// Each candidate's thumbnail is embedded and compared with the cut-out. It is
    /// the same comparison the catalogue uses, doing the opposite job: there it
    /// chose the nearest of a fixed set, here it throws out what Google dragged in.
    /// </summary>
    private async Task<List<VisualMatch>> VerifyAsync(
        DetectedItemEntity item, IReadOnlyList<VisualMatch> matches, CancellationToken ct)
    {
        var vector = await EmbedItemAsync(item, ct);
        if (vector is null) return matches.ToList();   // nothing to check against

        // Which bar applies depends on what the search was made with. The high bar
        // is for a plain crop of a worn photo, which is a photograph being compared
        // with photographs. A cut-out standing on nothing, and a flat-lay with half
        // a bedspread in it, are both further from a studio shot than that — same
        // garment, further apart in the arithmetic.
        var photographic = !string.IsNullOrWhiteSpace(item.CutoutImageId)
                           || item.FeedPost is { HasPerson: false };

        var bar = photographic
            ? _searchOptions.MinCutoutSimilarity
            : _searchOptions.MinSimilarity;

        // Measured on this model: a plausible garment match sits between 0.64 and
        // 0.92, and the wrong category falls away well below that.

        // Score here is likeness plus how much the picture looks like a catalogue
        // photograph, so the ordering is "the best picture of this garment" rather
        // than "the picture most like our snapshot".
        var scored = new List<(VisualMatch Match, double Score)>();
        var best = 0.0;
        var titleRejected = 0;

        foreach (var match in matches)
        {
            ct.ThrowIfCancellationRequested();

            // What the seller calls it, before what it looks like.
            if (!ProductCategories.TitleFits(item.Label, match.Title))
            {
                titleRejected++;
                continue;
            }

            var thumbnail = await _fetcher.FetchAsync(match.ThumbnailUrl, ct);
            if (thumbnail is null) continue;

            var candidate = await _embedder.EmbedAsync(thumbnail, ct);
            if (candidate is null) continue;

            var score = ClipEmbedder.Similarity(vector, candidate);
            if (score > best) best = score;

            if (score < bar) continue;

            // Sorted as a catalogue photograph first and a likeness second, not as
            // a weighted blend of the two. Our picture was cut out of a photograph
            // of a person, so another photograph of a person in the same jacket is
            // genuinely the closer likeness — and it is not what the tile is for.
            // Adding a bonus was not enough; likeness simply outvoted it.
            scored.Add((match, Rank(score, StudioLook.Score(thumbnail))));
        }

        if (scored.Count == 0)
        {
            _logger.LogInformation(
                "{Label}: {Total} results, {Titles} wrong kind, best likeness {Best:F3} "
                + "against a bar of {Bar:F2}.",
                item.Label, matches.Count, titleRejected, best, bar);
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(_options.Alternatives)
            .Select(x => x.Match)
            .ToList();
    }

    /// <summary>Whether this look holds exactly one detected thing.</summary>
    private async Task<bool> IsSingleItemAsync(DetectedItemEntity item, CancellationToken ct) =>
        await _db.DetectedItems.CountAsync(d => d.FeedPostId == item.FeedPostId, ct) == 1;

    /// <summary>
    /// Whether a plain crop is worth searching with. Either the segmenter has no
    /// class for this kind of thing at all, or the detector was sure enough that
    /// the failed mask is the mask's fault rather than evidence of nothing there.
    /// </summary>
    private static bool CanSearchByCrop(DetectedItemEntity item) =>
        ProductCategories.For(item.Label).Length > 0
        && (!GarmentCutoutService.IsSegmentable(item.Label) || item.Score >= 0.9);

    /// <summary>
    /// Crops the detection box out of the look and keeps it, returning its id.
    /// Padded a little, because a box drawn tight around a watch face loses the
    /// strap, and the strap is most of what identifies a watch.
    /// </summary>
    private async Task<string?> EnsureCropAsync(DetectedItemEntity item, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(item.CropImageId)) return item.CropImageId;

        var post = await _db.FeedPosts
            .AsNoTracking()
            .Where(p => p.Id == item.FeedPostId)
            .Select(p => p.ImageUrl)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(post)) return null;

        var photo = await _fetcher.FetchAsync(post, ct);
        if (photo is null) return null;

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(photo);

            const double pad = 0.18;
            var x = item.BoxX / 100 * image.Width;
            var y = item.BoxY / 100 * image.Height;
            var w = item.BoxWidth / 100 * image.Width;
            var h = item.BoxHeight / 100 * image.Height;

            var region = SixLabors.ImageSharp.Rectangle.Intersect(
                new SixLabors.ImageSharp.Rectangle(
                    (int)(x - w * pad), (int)(y - h * pad),
                    (int)(w * (1 + pad * 2)), (int)(h * (1 + pad * 2))),
                image.Bounds);

            if (region.Width < 48 || region.Height < 48) return null;

            using var crop = image.Clone(c => c.Crop(region));
            using var buffer = new MemoryStream();
            crop.Save(buffer, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 88 });

            var id = Guid.NewGuid().ToString("N");

            _db.UploadedImages.Add(new UploadedImageEntity
            {
                Id = id,
                OwnerKey = "system:crop",
                ContentType = "image/jpeg",
                Data = buffer.ToArray(),
                ByteSize = (int)buffer.Length,
                CreatedAt = DateTime.UtcNow
            });

            item.CropImageId = id;

            // Saved now: the search is about to be told where to fetch it from.
            await _db.SaveChangesAsync(ct);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not crop item {Id}.", item.Id);
            return null;
        }
    }

    /// <summary>
    /// Absolute URL for one of our images, as the outside world would fetch it —
    /// the search service has to be able to reach the picture we are asking about.
    /// </summary>
    private string? PublicUrlFor(string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId)) return null;

        var configured = _searchOptions.PublicBaseUrl;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return $"{configured.TrimEnd('/')}/api/images/{imageId}";
        }

        var request = _http.HttpContext?.Request;
        if (request is null) return null;

        // A loopback address is no use to a service that has to fetch the picture.
        if (request.Host.Host is "localhost" or "127.0.0.1") return null;

        return $"{request.Scheme}://{request.Host}/api/images/{imageId}";
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    /// <summary>
    /// The vector for a detected item: its cutout when there is one, since that is
    /// the garment alone, and otherwise nothing — a crop still full of background
    /// and forearm matches the background and the forearm.
    /// </summary>
    private async Task<float[]?> EmbedItemAsync(DetectedItemEntity item, CancellationToken ct)
    {
        var id = item.CutoutImageId ?? item.CropImageId;

        if (!string.IsNullOrWhiteSpace(id))
        {
            var stored = await _db.UploadedImages
                .AsNoTracking()
                .Where(i => i.Id == id)
                .Select(i => i.Data)
                .FirstOrDefaultAsync(ct);

            return stored is null ? null : await _embedder.EmbedAsync(stored, ct);
        }

        // A flat-lay has neither: the photograph is the garment, and it is what the
        // shops were shown. Without this every such item scored zero likeness
        // against every result, and the ordering fell back to studio look alone.
        var post = item.FeedPost?.ImageUrl
                   ?? await _db.FeedPosts
                       .AsNoTracking()
                       .Where(p => p.Id == item.FeedPostId && !p.HasPerson)
                       .Select(p => p.ImageUrl)
                       .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(post)) return null;

        var photo = await _fetcher.FetchAsync(post, ct);
        return photo is null ? null : await _embedder.EmbedAsync(photo, ct);
    }

    private async Task<List<CatalogueEntry>> LoadCatalogueAsync(CancellationToken ct)
    {
        var rows = await _db.Products
            .AsNoTracking()
            .Where(p => p.Embedding != null)
            .Select(p => new
            {
                p.ArticleType,
                p.Brand,
                p.Name,
                p.Colour,
                p.Price,
                p.ImageId,
                p.StoreUrl,
                p.Embedding
            })
            .ToListAsync(ct);

        return rows
            .Where(r => r.Embedding is not null)
            .Select(r => new CatalogueEntry(
                r.ArticleType, r.Brand, r.Name, r.Colour, r.Price, r.ImageId, r.StoreUrl, r.Embedding!))
            .ToList();
    }

    private record CatalogueEntry(
        string Category,
        string Brand,
        string Name,
        string Colour,
        decimal Price,
        string ImageId,
        string StoreUrl,
        float[] Embedding);
}
