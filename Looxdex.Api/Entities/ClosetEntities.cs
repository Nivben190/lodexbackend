using System.ComponentModel.DataAnnotations;

namespace Looxdex.Api.Entities;

public class ClosetItemEntity
{
    public int Id { get; set; }

    /// <summary>Anonymous device id today; becomes the authenticated user id in phase 1.</summary>
    [MaxLength(64)]
    public string OwnerKey { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string ImageUrl { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(60)]
    public string Color { get; set; } = string.Empty;

    [MaxLength(16)]
    public string ColorHex { get; set; } = "#000000";

    [MaxLength(60)]
    public string Season { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Brand { get; set; } = string.Empty;

    [MaxLength(60)]
    public string Formality { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>CLIP embedding used to match closet items against detected items.</summary>
    public float[]? Embedding { get; set; }

    public DateTime? EmbeddedAt { get; set; }
}

public class SuitcaseEntity
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string OwnerKey { get; set; } = string.Empty;

    [MaxLength(200)]
    public string TripName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Destination { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string CoverImageUrl { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public double ExpectedTempLow { get; set; }
    public double ExpectedTempHigh { get; set; }

    public List<EventGroupEntity> EventGroups { get; set; } = new();
}

public class EventGroupEntity
{
    public int Id { get; set; }

    public int SuitcaseId { get; set; }
    public SuitcaseEntity? Suitcase { get; set; }

    [MaxLength(60)]
    public string EventKey { get; set; } = string.Empty;

    [MaxLength(120)]
    public string EventLabel { get; set; } = string.Empty;

    [MaxLength(16)]
    public string Icon { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public List<PackingItemEntity> Items { get; set; } = new();
}

public class PackingItemEntity
{
    public int Id { get; set; }

    public int EventGroupId { get; set; }
    public EventGroupEntity? EventGroup { get; set; }

    public int? ClosetItemId { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string ImageUrl { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    public bool IsPacked { get; set; }
}
