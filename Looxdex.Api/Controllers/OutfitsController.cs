using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Models;
using Looxdex.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Controllers;

/// <summary>
/// Looks the wearer builds herself, out of the clothes she owns.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OutfitsController : ControllerBase
{
    /// <summary>
    /// Ceiling on a saved arrangement. A canvas of a dozen layers is a few KB;
    /// anything approaching this is not a look.
    /// </summary>
    private const int MaxCompositionBytes = 256 * 1024;

    private readonly LooxdexDbContext _db;
    private readonly IOwnerContext _owner;
    private readonly ILogger<OutfitsController> _logger;

    public OutfitsController(
        LooxdexDbContext db, IOwnerContext owner, ILogger<OutfitsController> logger)
    {
        _db = db;
        _owner = owner;
        _logger = logger;
    }

    /// <summary>The wearer's saved looks, most recent first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Outfit>>> GetAll(CancellationToken ct)
    {
        var ownerKey = _owner.OwnerKey;

        var rows = await _db.Outfits
            .AsNoTracking()
            .Where(o => o.OwnerKey == ownerKey)
            .OrderByDescending(o => o.UpdatedAt)
            .ToListAsync(ct);

        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Outfit>> GetById(int id, CancellationToken ct)
    {
        var ownerKey = _owner.OwnerKey;

        var outfit = await _db.Outfits
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.OwnerKey == ownerKey, ct);

        return outfit is null ? NotFound() : Ok(ToDto(outfit));
    }

    [HttpPost]
    public async Task<ActionResult<Outfit>> Create(
        [FromBody] SaveOutfitRequest request, CancellationToken ct)
    {
        if (request.Composition.Length > MaxCompositionBytes)
        {
            return BadRequest(new { error = "הלוק מורכב מדי לשמירה." });
        }

        var outfit = new OutfitEntity
        {
            OwnerKey = _owner.OwnerKey,
            Name = Name(request.Name),
            Background = string.IsNullOrWhiteSpace(request.Background) ? "#F2F0ED" : request.Background,
            PreviewImageId = request.PreviewImageId,
            Composition = request.Composition,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Outfits.Add(outfit);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Saved look {Id} for {Owner}.", outfit.Id, outfit.OwnerKey);

        return Ok(ToDto(outfit));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Outfit>> Update(
        int id, [FromBody] SaveOutfitRequest request, CancellationToken ct)
    {
        if (request.Composition.Length > MaxCompositionBytes)
        {
            return BadRequest(new { error = "הלוק מורכב מדי לשמירה." });
        }

        var ownerKey = _owner.OwnerKey;

        var outfit = await _db.Outfits
            .FirstOrDefaultAsync(o => o.Id == id && o.OwnerKey == ownerKey, ct);

        if (outfit is null) return NotFound();

        outfit.Name = Name(request.Name);
        outfit.Background = string.IsNullOrWhiteSpace(request.Background) ? outfit.Background : request.Background;
        outfit.Composition = request.Composition;
        outfit.UpdatedAt = DateTime.UtcNow;

        // A save without a fresh preview keeps the one it had, rather than losing
        // the thumbnail because the canvas could not be flattened this time.
        if (!string.IsNullOrWhiteSpace(request.PreviewImageId))
        {
            outfit.PreviewImageId = request.PreviewImageId;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(outfit));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ownerKey = _owner.OwnerKey;

        var outfit = await _db.Outfits
            .FirstOrDefaultAsync(o => o.Id == id && o.OwnerKey == ownerKey, ct);

        if (outfit is null) return NotFound();

        _db.Outfits.Remove(outfit);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static string Name(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "לוק חדש" : name.Trim();

    private Outfit ToDto(OutfitEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Background = e.Background,
        Composition = e.Composition,
        // Built from the request, so the same row serves localhost and production.
        PreviewUrl = string.IsNullOrWhiteSpace(e.PreviewImageId)
            ? null
            : $"{Request.Scheme}://{Request.Host}/api/images/{e.PreviewImageId}",
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
