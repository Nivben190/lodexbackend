using System.ComponentModel.DataAnnotations;

namespace Looxdex.Api.Entities;

/// <summary>Where a feed image was ingested from.</summary>
public enum FeedSource
{
    Seed = 0,
    Pexels = 1,
    Unsplash = 2,
    UserUpload = 3,

    /// <summary>
    /// A look the wearer brought in from Instagram: our copy of the image for
    /// analysis, and the creator's post embedded for credit.
    /// </summary>
    Instagram = 4
}

/// <summary>Progress of the fashion-detection pass over an image.</summary>
public enum DetectionState
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Skipped = 3
}

public class FeedPostEntity
{
    public int Id { get; set; }

    public FeedSource Source { get; set; }

    /// <summary>Id at the origin provider; used to avoid re-ingesting the same photo.</summary>
    [MaxLength(64)]
    public string ExternalId { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Smaller variant used by the masonry grid.</summary>
    [MaxLength(1024)]
    public string ThumbnailUrl { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Photographer { get; set; } = string.Empty;

    /// <summary>Attribution link back to the photographer — required by Pexels/Unsplash terms.</summary>
    [MaxLength(1024)]
    public string PhotographerUrl { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string SourceUrl { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Location { get; set; } = string.Empty;

    /// <summary>Free-text query this image was ingested for, e.g. "street style paris".</summary>
    [MaxLength(200)]
    public string Query { get; set; } = string.Empty;

    public int Likes { get; set; }
    public int AspectRatioWidth { get; set; } = 3;
    public int AspectRatioHeight { get; set; } = 4;

    public DetectionState DetectionState { get; set; } = DetectionState.Pending;
    public DateTime? DetectedAt { get; set; }
    public int DetectionAttempts { get; set; }

    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Stable ordering key for cursor pagination (descending).</summary>
    public long Rank { get; set; }

    /// <summary>
    /// Official embed markup from the provider, for showing the source post where
    /// it was published rather than restating it ourselves.
    ///
    /// Stored as returned: it is Instagram's own HTML, and rewriting it would both
    /// break the embed script and step outside what the oEmbed licence covers.
    /// Null for library photos, which carry a plain photographer credit instead.
    /// </summary>
    public string? EmbedHtml { get; set; }

    public List<DetectedItemEntity> DetectedItems { get; set; } = new();
}

public class DetectedItemEntity
{
    public int Id { get; set; }

    public int FeedPostId { get; set; }
    public FeedPostEntity? FeedPost { get; set; }

    /// <summary>Raw Fashionpedia label from the detector.</summary>
    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LabelHe { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Detector confidence, 0–1.</summary>
    public double Score { get; set; }

    /// <summary>Box as percentages of image size, matching what the Angular overlay expects.</summary>
    public double BoxX { get; set; }
    public double BoxY { get; set; }
    public double BoxWidth { get; set; }
    public double BoxHeight { get; set; }

    /// <summary>
    /// Id in <see cref="UploadedImageEntity"/> of the garment cut out of the photo,
    /// or null when the mask was unusable and the item falls back to a plain crop.
    /// Stored as an id rather than a URL so the host is never baked into the row —
    /// the same database serves localhost and production.
    /// </summary>
    [MaxLength(32)]
    public string? CutoutImageId { get; set; }

    /// <summary>
    /// When the cutout stage last looked at this item, whether or not it produced
    /// one. Without it a garment the segmenter cannot isolate is picked up by
    /// every following pass, and the backfill never moves past it.
    /// </summary>
    public DateTime? CutoutAttemptedAt { get; set; }

    /// <summary>
    /// When this item was last looked for in the shops, whether or not anything
    /// was found. Without it the same handful of unmatchable items is examined on
    /// every pass and the rest are never reached.
    /// </summary>
    public DateTime? MatchAttemptedAt { get; set; }

    /// <summary>Dominant colour of the cutout, in the closet's own colour vocabulary.</summary>
    [MaxLength(40)]
    public string? ColorName { get; set; }

    [MaxLength(9)]
    public string? ColorHex { get; set; }

    /// <summary>CLIP embedding of the cropped region; null until phase 4 has run.</summary>
    public float[]? Embedding { get; set; }

    public List<ShoppingAlternativeEntity> Alternatives { get; set; } = new();
}

public class ShoppingAlternativeEntity
{
    public int Id { get; set; }

    public int DetectedItemId { get; set; }
    public DetectedItemEntity? DetectedItem { get; set; }

    [MaxLength(120)]
    public string Brand { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>
    /// Currency the price is in. Shops answer in their own money, and a number
    /// with a shekel sign in front of it would simply be a lie.
    /// </summary>
    [MaxLength(8)]
    public string Currency { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string ImageUrl { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string StoreUrl { get; set; } = string.Empty;
}

/// <summary>
/// A post saved by one owner. Replaces the old global <c>FeedPost.IsSaved</c> flag,
/// which was shared by every visitor to the site.
/// </summary>
public class SavedPostEntity
{
    public int Id { get; set; }

    /// <summary>Anonymous device id today; becomes the authenticated user id in phase 1.</summary>
    [MaxLength(64)]
    public string OwnerKey { get; set; } = string.Empty;

    public int FeedPostId { get; set; }
    public FeedPostEntity? FeedPost { get; set; }

    /// <summary>
    /// Folder the save was filed under, e.g. "פריז". Null means unfiled, which
    /// is what the "הכל" tab shows alongside everything else.
    /// </summary>
    [MaxLength(60)]
    public string? Folder { get; set; }

    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
