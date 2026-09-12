using System.ComponentModel.DataAnnotations;

namespace Looxdex.Api.Entities;

/// <summary>Where a feed image was ingested from.</summary>
public enum FeedSource
{
    Seed = 0,
    Pexels = 1,
    Unsplash = 2,
    UserUpload = 3
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
