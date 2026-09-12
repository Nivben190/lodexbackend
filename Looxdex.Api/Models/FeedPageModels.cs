namespace Looxdex.Api.Models;

/// <summary>One page of feed posts plus the cursor that fetches the next one.</summary>
public class FeedPage
{
    public List<FeedPost> Items { get; set; } = new();

    /// <summary>Opaque cursor for the next page; null when the end is reached.</summary>
    public string? NextCursor { get; set; }

    public bool HasMore => NextCursor is not null;

    /// <summary>Total posts in the library, for display only.</summary>
    public int TotalCount { get; set; }
}
