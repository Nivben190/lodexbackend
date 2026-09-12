using Looxdex.Api.Configuration;
using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Services.Detection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Matching;

/// <param name="ExternalId">Id in the source catalogue.</param>
public record ProductImport(
    string ExternalId,
    string Source,
    string Name,
    string Brand,
    string ArticleType,
    string Colour,
    string Gender,
    decimal Price,
    string ImageUrl,
    string StoreUrl);

/// <summary>
/// Fills and searches the shoppable catalogue.
///
/// Ingest is deliberately one product at a time over HTTP: whatever the source is
/// — a dataset today, an affiliate feed tomorrow — the app only has to know how to
/// be handed a garment, fetch its photograph and remember what it looks like.
/// </summary>
public class ProductCatalogue
{
    private readonly LooxdexDbContext _db;
    private readonly IClipEmbedder _embedder;
    private readonly ImageFetcher _fetcher;
    private readonly ILogger<ProductCatalogue> _logger;

    public ProductCatalogue(
        LooxdexDbContext db,
        IClipEmbedder embedder,
        ImageFetcher fetcher,
        ILogger<ProductCatalogue> logger)
    {
        _db = db;
        _embedder = embedder;
        _fetcher = fetcher;
        _logger = logger;
    }

    /// <summary>
    /// Adds one product, fetching and embedding its photograph. Returns false when
    /// the picture could not be had — a catalogue entry without one is useless,
    /// since the picture is the entire point.
    /// </summary>
    public async Task<bool> AddAsync(ProductImport import, CancellationToken ct)
    {
        var exists = await _db.Products
            .AnyAsync(p => p.Source == import.Source && p.ExternalId == import.ExternalId, ct);

        if (exists) return true;

        var bytes = await _fetcher.FetchAsync(import.ImageUrl, ct);
        if (bytes is null) return false;

        var contentType = StoredImages.SniffContentType(bytes);
        if (contentType is null) return false;

        var embedding = await _embedder.EmbedAsync(bytes, ct);
        if (embedding is null) return false;

        // A feed that brings its own names keeps them. This one does not, so the
        // catalogue names the garment itself, in the same words the rest of the app
        // uses — which reads better beside a detected item than a shop's SEO title
        // would anyway.
        var colour = import.Colour;
        if (string.IsNullOrWhiteSpace(colour)) colour = DescribeColour(bytes);

        var name = import.Name;
        if (string.IsNullOrWhiteSpace(name)) name = Describe(import.ArticleType, colour);

        var imageId = Guid.NewGuid().ToString("N");

        _db.UploadedImages.Add(new UploadedImageEntity
        {
            Id = imageId,
            OwnerKey = "system:product",
            ContentType = contentType,
            Data = bytes,
            ByteSize = bytes.Length,
            CreatedAt = DateTime.UtcNow
        });

        _db.Products.Add(new ProductEntity
        {
            ExternalId = import.ExternalId,
            Source = import.Source,
            Name = Trim(name, 200),
            Brand = Trim(import.Brand, 120),
            ArticleType = Trim(import.ArticleType, 80),
            Colour = Trim(colour, 60),
            Gender = Trim(import.Gender, 30),
            Price = import.Price,
            ImageId = imageId,
            StoreUrl = Trim(import.StoreUrl, 1024),
            Embedding = embedding,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Hebrew name for a catalogue garment: what it is, and what colour.</summary>
    private static string Describe(string articleType, string colour)
    {
        var noun = articleType switch
        {
            "Topwear" => ("חולצה", HebrewForm.FeminineSingular),
            "Bottomwear" => ("מכנסיים", HebrewForm.MasculinePlural),
            "Dress" => ("שמלה", HebrewForm.FeminineSingular),
            "Shoes" or "Sandal" or "Flip Flops" => ("נעליים", HebrewForm.FemininePlural),
            "Bags" or "Wallets" => ("תיק", HebrewForm.MasculineSingular),
            "Belts" => ("חגורה", HebrewForm.FeminineSingular),
            "Watches" => ("שעון", HebrewForm.MasculineSingular),
            "Eyewear" => ("משקפיים", HebrewForm.MasculinePlural),
            "Headwear" => ("כובע", HebrewForm.MasculineSingular),
            "Scarves" or "Stoles" => ("צעיף", HebrewForm.MasculineSingular),
            "Socks" => ("גרביים", HebrewForm.MasculinePlural),
            "Ties" => ("עניבה", HebrewForm.FeminineSingular),
            _ => ("פריט", HebrewForm.MasculineSingular)
        };

        var adjective = ColorNamer.Inflect(colour, noun.Item2);
        return string.IsNullOrWhiteSpace(adjective) ? noun.Item1 : $"{noun.Item1} {adjective}";
    }

    /// <summary>
    /// The garment's colour, read from the middle of the photograph. A product shot
    /// is the garment on white, so the centre is cloth and the corners are paper.
    /// </summary>
    private static string DescribeColour(byte[] bytes)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(bytes);

            long r = 0, g = 0, b = 0, n = 0;

            var fromX = image.Width / 4;
            var toX = image.Width * 3 / 4;
            var fromY = image.Height / 4;
            var toY = image.Height * 3 / 4;

            for (var y = fromY; y < toY; y++)
            for (var x = fromX; x < toX; x++)
            {
                var pixel = image[x, y];

                // Skip the backdrop, or every garment comes out white.
                if (pixel.R > 242 && pixel.G > 242 && pixel.B > 242) continue;

                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
                n++;
            }

            if (n == 0) return string.Empty;

            return ColorNamer.Describe((byte)(r / n), (byte)(g / n), (byte)(b / n)).Name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

/// <summary>
/// What a detected garment may be matched against.
///
/// The comparison itself is CLIP's, on pixels; this only stops it comparing across
/// the obvious divides. Without it a leopard coat finds a leopard-print scarf,
/// which is a fine picture and the wrong item.
/// </summary>
public static class ProductCategories
{
    private static readonly Dictionary<string, string[]> ByLabel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shirt, blouse"] = new[] { "Topwear" },
        ["top, t-shirt, sweatshirt"] = new[] { "Topwear" },
        ["sweater"] = new[] { "Topwear" },
        ["cardigan"] = new[] { "Topwear" },
        ["jacket"] = new[] { "Topwear" },
        ["coat"] = new[] { "Topwear" },
        ["cape"] = new[] { "Topwear" },
        ["vest"] = new[] { "Topwear" },
        ["dress"] = new[] { "Dress", "Topwear" },
        ["jumpsuit"] = new[] { "Dress", "Topwear" },
        ["pants"] = new[] { "Bottomwear" },
        ["shorts"] = new[] { "Bottomwear" },
        ["skirt"] = new[] { "Bottomwear", "Dress" },
        ["shoe"] = new[] { "Shoes", "Sandal", "Flip Flops" },
        ["sock"] = new[] { "Socks" },
        ["tights, stockings"] = new[] { "Socks", "Bottomwear" },
        ["bag, wallet"] = new[] { "Bags", "Wallets" },
        ["belt"] = new[] { "Belts" },
        ["scarf"] = new[] { "Scarves", "Stoles" },
        ["hat"] = new[] { "Headwear" },
        ["glasses"] = new[] { "Eyewear" },
        ["watch"] = new[] { "Watches" },
        ["tie"] = new[] { "Ties" },
        ["glove"] = new[] { "Gloves" },
        ["headband, head covering, hair accessory"] = new[] { "Headwear", "Hair Accessory" }
    };

    /// <summary>Catalogue categories a detection may be matched within, or empty for none.</summary>
    public static string[] For(string? label) =>
        label is not null && ByLabel.TryGetValue(label.Trim(), out var types)
            ? types
            : Array.Empty<string>();
}
