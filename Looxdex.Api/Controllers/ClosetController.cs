using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Models;
using Looxdex.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClosetController : ControllerBase
{
    private const string FallbackImage =
        "https://images.unsplash.com/photo-1445205170230-053b83016050?w=400&q=80";

    private readonly LooxdexDbContext _db;
    private readonly IOwnerContext _owner;
    private readonly StarterClosetService _starter;

    public ClosetController(
        LooxdexDbContext db, IOwnerContext owner, StarterClosetService starter)
    {
        _db = db;
        _owner = owner;
        _starter = starter;
    }

    /// <param name="wishlist">
    /// false (default) returns owned pieces, true returns the wishlist. The two
    /// are separate tabs in the wardrobe, so a request never mixes them.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClosetItem>>> GetCloset(
        [FromQuery] string? category,
        [FromQuery] string? color,
        [FromQuery] string? season,
        [FromQuery] bool wishlist = false,
        CancellationToken ct = default)
    {
        // First visit gets a small starter closet so the app is never empty.
        await _starter.EnsureSeededAsync(_owner.OwnerKey, ct);

        var query = _db.ClosetItems
            .AsNoTracking()
            .Where(i => i.OwnerKey == _owner.OwnerKey && i.IsWishlist == wishlist);

        if (!string.IsNullOrWhiteSpace(category) && category != "הכל")
            query = query.Where(i => i.Category == category);

        if (!string.IsNullOrWhiteSpace(color))
            query = query.Where(i => i.Color == color);

        if (!string.IsNullOrWhiteSpace(season))
            query = query.Where(i => i.Season == season);

        var items = await query
            .OrderByDescending(i => i.AddedAt)
            .ToListAsync(ct);

        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClosetItem>> GetById(int id, CancellationToken ct)
    {
        var item = await _db.ClosetItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerKey == _owner.OwnerKey, ct);

        return item is null ? NotFound() : Ok(ToDto(item));
    }

    [HttpPost]
    public async Task<ActionResult<ClosetItem>> AddItem(
        [FromBody] CreateClosetItemRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "שם הפריט הוא שדה חובה." });
        }

        var entity = new ClosetItemEntity
        {
            OwnerKey = _owner.OwnerKey,
            Name = request.Name.Trim(),
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? FallbackImage : request.ImageUrl,
            Category = request.Category,
            Color = request.Color,
            ColorHex = request.ColorHex,
            Season = request.Season,
            Brand = request.Brand,
            Formality = request.Formality,
            IsWishlist = request.IsWishlist,
            AddedAt = DateTime.UtcNow
        };

        _db.ClosetItems.Add(entity);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity));
    }

    /// <summary>Moves an item between the wishlist and the closet proper.</summary>
    [HttpPatch("{id:int}/wishlist")]
    public async Task<ActionResult<ClosetItem>> SetWishlist(
        int id, [FromQuery] bool value, CancellationToken ct)
    {
        var item = await _db.ClosetItems
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerKey == _owner.OwnerKey, ct);

        if (item is null) return NotFound();

        item.IsWishlist = value;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(item));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteItem(int id, CancellationToken ct)
    {
        var item = await _db.ClosetItems
            .FirstOrDefaultAsync(i => i.Id == id && i.OwnerKey == _owner.OwnerKey, ct);

        if (item is null) return NotFound();

        _db.ClosetItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ClosetItem ToDto(ClosetItemEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        ImageUrl = e.ImageUrl,
        Category = e.Category,
        Color = e.Color,
        ColorHex = e.ColorHex,
        Season = e.Season,
        Brand = e.Brand,
        Formality = e.Formality,
        IsFavorite = e.IsFavorite,
        IsWishlist = e.IsWishlist,
        AddedAt = e.AddedAt
    };
}
