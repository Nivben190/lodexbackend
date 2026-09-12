using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Models;
using Looxdex.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuitcaseController : ControllerBase
{
    private readonly LooxdexDbContext _db;
    private readonly IOwnerContext _owner;

    public SuitcaseController(LooxdexDbContext db, IOwnerContext owner)
    {
        _db = db;
        _owner = owner;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Suitcase>>> GetSuitcases(CancellationToken ct)
    {
        var suitcases = await LoadForOwnerAsync().ToListAsync(ct);
        return Ok(suitcases.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Suitcase>> GetById(int id, CancellationToken ct)
    {
        var suitcase = await LoadForOwnerAsync().FirstOrDefaultAsync(s => s.Id == id, ct);
        return suitcase is null ? NotFound() : Ok(ToDto(suitcase));
    }

    [HttpPatch("toggle-packed")]
    public async Task<ActionResult<Suitcase>> TogglePacked(
        [FromBody] TogglePackedRequest request, CancellationToken ct)
    {
        var suitcase = await LoadForOwnerAsync(tracking: true)
            .FirstOrDefaultAsync(s => s.Id == request.SuitcaseId, ct);

        if (suitcase is null) return NotFound();

        var item = suitcase.EventGroups
            .SelectMany(g => g.Items)
            .FirstOrDefault(i => i.Id == request.PackingItemId);

        if (item is null) return NotFound();

        item.IsPacked = request.IsPacked;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(suitcase));
    }

    /// <summary>
    /// Adds the closet items detected in a saved look to one event group.
    /// Matching now comes from the detection pipeline rather than hardcoded ids.
    /// </summary>
    [HttpPost("{id:int}/add-look")]
    public async Task<ActionResult<AddLookToSuitcaseResult>> AddLook(
        int id, [FromBody] AddLookToSuitcaseRequest request, CancellationToken ct)
    {
        var suitcase = await LoadForOwnerAsync(tracking: true)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (suitcase is null) return NotFound();

        var group = suitcase.EventGroups.FirstOrDefault(g => g.EventKey == request.EventKey);
        if (group is null) return NotFound();

        var post = await _db.FeedPosts
            .AsNoTracking()
            .Include(p => p.DetectedItems)
            .FirstOrDefaultAsync(p => p.Id == request.PostId, ct);

        if (post is null) return NotFound();

        // Take the categories detected in the look, then pull the owner's closet
        // items in those categories that are not already packed in this group.
        var categories = post.DetectedItems
            .Select(d => d.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        if (categories.Count == 0)
        {
            return Ok(new AddLookToSuitcaseResult { Suitcase = ToDto(suitcase), AddedCount = 0 });
        }

        var alreadyPacked = group.Items
            .Where(i => i.ClosetItemId.HasValue)
            .Select(i => i.ClosetItemId!.Value)
            .ToHashSet();

        var candidates = await _db.ClosetItems
            .AsNoTracking()
            .Where(c => c.OwnerKey == _owner.OwnerKey && categories.Contains(c.Category))
            .ToListAsync(ct);

        var addedCount = 0;

        // One item per detected category, favourites first.
        foreach (var category in categories)
        {
            var pick = candidates
                .Where(c => c.Category == category && !alreadyPacked.Contains(c.Id))
                .OrderByDescending(c => c.IsFavorite)
                .ThenByDescending(c => c.AddedAt)
                .FirstOrDefault();

            if (pick is null) continue;

            group.Items.Add(new PackingItemEntity
            {
                ClosetItemId = pick.Id,
                Name = pick.Name,
                ImageUrl = pick.ImageUrl,
                Category = pick.Category,
                IsPacked = false
            });

            alreadyPacked.Add(pick.Id);
            addedCount++;
        }

        if (addedCount > 0) await _db.SaveChangesAsync(ct);

        return Ok(new AddLookToSuitcaseResult { Suitcase = ToDto(suitcase), AddedCount = addedCount });
    }

    private IQueryable<SuitcaseEntity> LoadForOwnerAsync(bool tracking = false)
    {
        var query = _db.Suitcases
            .Where(s => s.OwnerKey == _owner.OwnerKey)
            .Include(s => s.EventGroups.OrderBy(g => g.SortOrder))
                .ThenInclude(g => g.Items)
            .AsQueryable();

        return tracking ? query : query.AsNoTracking();
    }

    private static Suitcase ToDto(SuitcaseEntity e)
    {
        var groups = e.EventGroups
            .OrderBy(g => g.SortOrder)
            .Select(g => new OutfitEventGroup
            {
                EventKey = g.EventKey,
                EventLabel = g.EventLabel,
                Icon = g.Icon,
                Items = g.Items.Select(i => new PackingItem
                {
                    Id = i.Id,
                    ClosetItemId = i.ClosetItemId ?? 0,
                    Name = i.Name,
                    ImageUrl = i.ImageUrl,
                    Category = i.Category,
                    IsPacked = i.IsPacked
                }).ToList()
            })
            .ToList();

        // TotalItems / PackedItems / PackedPercentage are computed from EventGroups.
        return new Suitcase
        {
            Id = e.Id,
            TripName = e.TripName,
            Destination = e.Destination,
            CoverImageUrl = e.CoverImageUrl,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            ExpectedTempLow = e.ExpectedTempLow,
            ExpectedTempHigh = e.ExpectedTempHigh,
            EventGroups = groups
        };
    }
}
