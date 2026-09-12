using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Services;

/// <summary>Serves the feed from our own database, so provider rate limits never touch a user request.</summary>
public class FeedReadService
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 60;

    private readonly LooxdexDbContext _db;
    private readonly IOwnerContext _owner;

    public FeedReadService(LooxdexDbContext db, IOwnerContext owner)
    {
        _db = db;
        _owner = owner;
    }

    public async Task<FeedPage> GetPageAsync(
        string? cursor, int? limit, string? search, bool savedOnly, CancellationToken ct)
    {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var ownerKey = _owner.OwnerKey;

        var savedIds = await _db.SavedPosts
            .Where(s => s.OwnerKey == ownerKey)
            .Select(s => s.FeedPostId)
            .ToHashSetAsync(ct);

        var query = _db.FeedPosts
            .AsNoTracking()
            .Include(p => p.DetectedItems)
                .ThenInclude(d => d.Alternatives)
            .AsQueryable();

        if (savedOnly)
        {
            // Materialised above, so this stays a single translated IN clause.
            query = query.Where(p => savedIds.Contains(p.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Title.ToLower().Contains(term) ||
                p.Photographer.ToLower().Contains(term) ||
                p.Query.ToLower().Contains(term) ||
                p.DetectedItems.Any(d =>
                    d.LabelHe.ToLower().Contains(term) || d.Label.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(ct);

        // Keyset pagination on a descending Rank. Unlike OFFSET this cannot skip or
        // repeat rows when new posts are ingested mid-scroll.
        if (TryParseCursor(cursor, out var afterRank, out var afterId))
        {
            query = query.Where(p => p.Rank < afterRank || (p.Rank == afterRank && p.Id < afterId));
        }

        var rows = await query
            .OrderByDescending(p => p.Rank)
            .ThenByDescending(p => p.Id)
            .Take(pageSize + 1)   // one extra tells us whether another page exists
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var last = rows.LastOrDefault();

        return new FeedPage
        {
            Items = rows.Select(r => ToDto(r, savedIds.Contains(r.Id))).ToList(),
            NextCursor = hasMore && last is not null ? $"{last.Rank}_{last.Id}" : null,
            TotalCount = total
        };
    }

    public async Task<FeedPost?> GetByIdAsync(int id, CancellationToken ct)
    {
        var ownerKey = _owner.OwnerKey;

        var entity = await _db.FeedPosts
            .AsNoTracking()
            .Include(p => p.DetectedItems)
                .ThenInclude(d => d.Alternatives)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null) return null;

        var isSaved = await _db.SavedPosts
            .AnyAsync(s => s.OwnerKey == ownerKey && s.FeedPostId == id, ct);

        return ToDto(entity, isSaved);
    }

    /// <summary>Adds or removes this owner's save. Returns null when the post does not exist.</summary>
    public async Task<FeedPost?> ToggleSaveAsync(int id, CancellationToken ct)
    {
        var ownerKey = _owner.OwnerKey;

        var entity = await _db.FeedPosts
            .Include(p => p.DetectedItems)
                .ThenInclude(d => d.Alternatives)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null) return null;

        var existing = await _db.SavedPosts
            .FirstOrDefaultAsync(s => s.OwnerKey == ownerKey && s.FeedPostId == id, ct);

        bool isSavedNow;

        if (existing is null)
        {
            _db.SavedPosts.Add(new SavedPostEntity
            {
                OwnerKey = ownerKey,
                FeedPostId = id,
                SavedAt = DateTime.UtcNow
            });
            isSavedNow = true;
        }
        else
        {
            _db.SavedPosts.Remove(existing);
            isSavedNow = false;
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(entity, isSavedNow);
    }

    private static bool TryParseCursor(string? cursor, out long rank, out int id)
    {
        rank = 0;
        id = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return false;

        var parts = cursor.Split('_', 2);
        return parts.Length == 2
               && long.TryParse(parts[0], out rank)
               && int.TryParse(parts[1], out id);
    }

    private static FeedPost ToDto(FeedPostEntity e, bool isSaved) => new()
    {
        Id = e.Id,
        ImageUrl = e.ImageUrl,
        ThumbnailUrl = string.IsNullOrWhiteSpace(e.ThumbnailUrl) ? e.ImageUrl : e.ThumbnailUrl,
        Title = e.Title,
        Photographer = e.Photographer,
        PhotographerUrl = e.PhotographerUrl,
        SourceUrl = e.SourceUrl,
        Location = e.Location,
        Likes = e.Likes,
        AspectRatioWidth = e.AspectRatioWidth,
        AspectRatioHeight = e.AspectRatioHeight,
        IsSaved = isSaved,
        IsAnalyzed = e.DetectionState == DetectionState.Completed,
        DetectedItems = e.DetectedItems
            .OrderByDescending(d => d.Score)
            .Select(d => new DetectedItem
            {
                Id = d.Id,
                Label = d.Label,
                LabelHe = d.LabelHe,
                Category = d.Category,
                Score = d.Score,
                Box = new BoundingBox
                {
                    X = d.BoxX,
                    Y = d.BoxY,
                    Width = d.BoxWidth,
                    Height = d.BoxHeight
                },
                Alternatives = d.Alternatives.Select(a => new ShoppingAlternative
                {
                    Id = a.Id,
                    Brand = a.Brand,
                    Name = a.Name,
                    Price = a.Price,
                    ImageUrl = a.ImageUrl,
                    StoreUrl = a.StoreUrl
                }).ToList()
            })
            .ToList()
    };
}
