namespace Looxdex.Api.Models;

public class PackingItem
{
    public int Id { get; set; }
    public int ClosetItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsPacked { get; set; }
}

public class OutfitEventGroup
{
    public string EventKey { get; set; } = string.Empty;
    public string EventLabel { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<PackingItem> Items { get; set; } = new();
}

public class Suitcase
{
    public int Id { get; set; }
    public string TripName { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string CoverImageUrl { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public double ExpectedTempLow { get; set; }
    public double ExpectedTempHigh { get; set; }
    public List<OutfitEventGroup> EventGroups { get; set; } = new();

    public int TotalItems => EventGroups.Sum(g => g.Items.Count);
    public int PackedItems => EventGroups.Sum(g => g.Items.Count(i => i.IsPacked));
    public double PackedPercentage => TotalItems == 0 ? 0 : Math.Round(PackedItems * 100.0 / TotalItems, 0);
}

public class TogglePackedRequest
{
    public int SuitcaseId { get; set; }
    public int PackingItemId { get; set; }
    public bool IsPacked { get; set; }
}

public class AddLookToSuitcaseRequest
{
    public int PostId { get; set; }
    public string EventKey { get; set; } = string.Empty;
}

public class AddLookToSuitcaseResult
{
    public Suitcase Suitcase { get; set; } = null!;
    public int AddedCount { get; set; }
}
