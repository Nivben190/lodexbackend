using Looxdex.Api.Configuration;
using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Services.Detection;
using Microsoft.EntityFrameworkCore;
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
            .Where(d => force || !d.Alternatives.Any())
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

            if (force && item.Alternatives.Count > 0)
            {
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
    /// Looks the garment up in the shops. Returns whether anything was found.
    /// </summary>
    private async Task<bool> MatchOnlineAsync(DetectedItemEntity item, CancellationToken ct)
    {
        // A detection the detector half believed is usually a garment seen from
        // behind, or half of one. Searching the shops for it spends a lookup to
        // find something that looks like whatever the mask happened to keep — a
        // shirt with a bag strap across it comes back as a holster, correctly.
        if (item.Score < _searchOptions.MinDetectionScore) return false;

        var imageUrl = PublicUrlFor(item.CutoutImageId);

        // Only a cutout is worth sending. A crop still holding pavement and a
        // forearm comes back with pavement and forearms.
        if (imageUrl is null) return false;

        var matches = await _visualSearch.FindAsync(imageUrl, ct);
        if (matches.Count == 0) return false;

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

        foreach (var match in matches)
        {
            _db.ShoppingAlternatives.Add(new ShoppingAlternativeEntity
            {
                DetectedItemId = item.Id,
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

        var scored = new List<(VisualMatch Match, double Score)>();

        foreach (var match in matches)
        {
            ct.ThrowIfCancellationRequested();

            // What the seller calls it, before what it looks like.
            if (!ProductCategories.TitleFits(item.Label, match.Title)) continue;

            var thumbnail = await _fetcher.FetchAsync(match.ThumbnailUrl, ct);
            if (thumbnail is null) continue;

            var candidate = await _embedder.EmbedAsync(thumbnail, ct);
            if (candidate is null) continue;

            var score = ClipEmbedder.Similarity(vector, candidate);
            if (score >= _searchOptions.MinSimilarity) scored.Add((match, score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(_options.Alternatives)
            .Select(x => x.Match)
            .ToList();
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
        if (string.IsNullOrWhiteSpace(item.CutoutImageId)) return null;

        var image = await _db.UploadedImages
            .AsNoTracking()
            .Where(i => i.Id == item.CutoutImageId)
            .Select(i => i.Data)
            .FirstOrDefaultAsync(ct);

        return image is null ? null : await _embedder.EmbedAsync(image, ct);
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
