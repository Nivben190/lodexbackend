using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Models;
using Looxdex.Api.Services.Detection;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Services;

/// <summary>Serves the feed from our own database, so provider rate limits never touch a user request.</summary>
public class FeedReadService
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 60;

    private readonly LooxdexDbContext _db;
    private readonly IOwnerContext _owner;
    private readonly IHttpContextAccessor _http;

    public FeedReadService(LooxdexDbContext db, IOwnerContext owner, IHttpContextAccessor http)
    {
        _db = db;
        _owner = owner;
        _http = http;
    }

    public async Task<FeedPage> GetPageAsync(
        string? cursor,
        int? limit,
        string? search,
        bool savedOnly,
        string? folder,
        CancellationToken ct)
    {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var ownerKey = _owner.OwnerKey;

        var savedIds = await _db.SavedPosts
            .Where(s => s.OwnerKey == ownerKey)
            .Select(s => s.FeedPostId)
            .ToHashSetAsync(ct);

        // Folder narrows the saved set; it is meaningless outside the saved view.
        var folderIds = savedOnly && !string.IsNullOrWhiteSpace(folder)
            ? await _db.SavedPosts
                .Where(s => s.OwnerKey == ownerKey && s.Folder == folder)
                .Select(s => s.FeedPostId)
                .ToHashSetAsync(ct)
            : null;

        var query = _db.FeedPosts
            .AsNoTracking()
            .Include(p => p.DetectedItems)
                .ThenInclude(d => d.Alternatives)
            .AsQueryable();

        if (savedOnly)
        {
            // Materialised above, so this stays a single translated IN clause.
            var ids = folderIds ?? savedIds;
            query = query.Where(p => ids.Contains(p.Id));
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

    /// <summary>
    /// Files a saved look under a folder, or clears it when folder is null.
    /// Saves the post first if it was not already saved, so filing doubles as saving.
    /// </summary>
    public async Task<bool> SetFolderAsync(int postId, string? folder, CancellationToken ct)
    {
        var ownerKey = _owner.OwnerKey;

        if (!await _db.FeedPosts.AnyAsync(p => p.Id == postId, ct)) return false;

        var saved = await _db.SavedPosts
            .FirstOrDefaultAsync(s => s.OwnerKey == ownerKey && s.FeedPostId == postId, ct);

        if (saved is null)
        {
            saved = new SavedPostEntity
            {
                OwnerKey = ownerKey,
                FeedPostId = postId,
                SavedAt = DateTime.UtcNow
            };
            _db.SavedPosts.Add(saved);
        }

        saved.Folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Folders in use, with how many looks each holds.</summary>
    public async Task<List<SavedFolder>> GetFoldersAsync(CancellationToken ct)
    {
        var ownerKey = _owner.OwnerKey;

        return await _db.SavedPosts
            .Where(s => s.OwnerKey == ownerKey && s.Folder != null)
            .GroupBy(s => s.Folder!)
            .Select(g => new SavedFolder { Name = g.Key, Count = g.Count() })
            .OrderByDescending(f => f.Count)
            .ToListAsync(ct);
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

    /// <summary>
    /// Absolute URL for a stored image.
    ///
    /// Built from the current request rather than a configured host: the same
    /// database is served by localhost during development and by the deployed API
    /// in production, so a host baked into the row would be wrong for one of them.
    /// </summary>
    private string? ImageUrlFor(string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId)) return null;

        var request = _http.HttpContext?.Request;
        if (request is null) return $"/api/images/{imageId}";

        return $"{request.Scheme}://{request.Host}/api/images/{imageId}";
    }

    /// <summary>
    /// What to call the item in a list: the garment and its colour, agreeing in
    /// gender and number, e.g. "מכנסיים כחולים" rather than "מכנסיים כחול".
    /// </summary>
    private static string ProductName(DetectedItemEntity item)
    {
        var label = FashionpediaLabels.Resolve(item.Label);
        var noun = label?.ProductName ?? item.LabelHe;

        if (string.IsNullOrWhiteSpace(item.ColorName)) return noun;

        var adjective = ColorNamer.Inflect(
            item.ColorName, label?.Form ?? HebrewForm.MasculineSingular);

        return string.IsNullOrWhiteSpace(adjective) ? noun : $"{noun} {adjective}";
    }

    private FeedPost ToDto(FeedPostEntity e, bool isSaved) => new()
    {
        Id = e.Id,
        ImageUrl = e.ImageUrl,
        ThumbnailUrl = string.IsNullOrWhiteSpace(e.ThumbnailUrl) ? e.ImageUrl : e.ThumbnailUrl,
        Title = e.Title,
        Photographer = e.Photographer,
        PhotographerUrl = e.PhotographerUrl,
        SourceUrl = e.SourceUrl,
        EmbedHtml = e.EmbedHtml,
        Source = e.Source.ToString().ToLowerInvariant(),
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
                CutoutUrl = ImageUrlFor(d.CutoutImageId),
                ColorName = d.ColorName,
                ColorHex = d.ColorHex,
                DisplayName = ProductName(d),
                Subtitle = d.Category == d.LabelHe ? d.Category : $"{d.Category} · {d.LabelHe}",
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
