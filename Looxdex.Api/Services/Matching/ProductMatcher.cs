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
    private readonly ProductMatchOptions _options;
    private readonly ILogger<ProductMatcher> _logger;

    public ProductMatcher(
        LooxdexDbContext db,
        IClipEmbedder embedder,
        IOptions<ProductMatchOptions> options,
        ILogger<ProductMatcher> logger)
    {
        _db = db;
        _embedder = embedder;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled && _embedder.Enabled;

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
