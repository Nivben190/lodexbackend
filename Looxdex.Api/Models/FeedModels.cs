namespace Looxdex.Api.Models;

public class BoundingBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class ShoppingAlternative
{
    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string StoreUrl { get; set; } = string.Empty;
}

public class DetectedItem
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string LabelHe { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Detector confidence, 0–1.</summary>
    public double Score { get; set; }

    public BoundingBox Box { get; set; } = new();

    /// <summary>
    /// The garment cut out of the photo on a transparent tile, or null when the
    /// mask was not good enough and the client should fall back to the box crop.
    /// </summary>
    public string? CutoutUrl { get; set; }

    /// <summary>
    /// A shop's photograph of the nearest thing in the catalogue. This is what the
    /// tile shows when there is one: a garment worn in a street photo can be cut
    /// out but never turned into a product shot, and the catalogue is full of
    /// product shots.
    /// </summary>
    public string? ProductUrl { get; set; }

    /// <summary>Colour in the closet's vocabulary, e.g. "לבן"; null before a cutout exists.</summary>
    public string? ColorName { get; set; }

    public string? ColorHex { get; set; }

    /// <summary>
    /// What to call the item in a list: colour and garment, the way a shop would
    /// title it. Falls back to the garment alone when the colour is unknown.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Closet category and garment type, for the caption under the tile.</summary>
    public string Subtitle { get; set; } = string.Empty;

    public bool OwnedInCloset { get; set; }
    public int? MatchingClosetItemId { get; set; }
    public List<int> SimilarClosetItemIds { get; set; } = new();
    public List<ShoppingAlternative> Alternatives { get; set; } = new();
}

public class FeedPost
{
    public int Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Smaller variant for the masonry grid; falls back to ImageUrl.</summary>
    public string ThumbnailUrl { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Photographer { get; set; } = string.Empty;

    /// <summary>Attribution link, required by the image provider's terms.</summary>
    public string PhotographerUrl { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Provider embed markup, when the look came from a post that should be shown
    /// where it was published. Null for library photos.
    /// </summary>
    public string? EmbedHtml { get; set; }

    /// <summary>Where the look came from, lowercased: "instagram", "pexels", "userupload".</summary>
    public string Source { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;
    public int Likes { get; set; }
    public int AspectRatioHeight { get; set; } = 4;
    public int AspectRatioWidth { get; set; } = 3;
    public bool IsSaved { get; set; }

    /// <summary>False while detection is still pending for this image.</summary>
    public bool IsAnalyzed { get; set; }

    public List<DetectedItem> DetectedItems { get; set; } = new();
}
