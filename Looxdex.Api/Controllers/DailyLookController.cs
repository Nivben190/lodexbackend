using Looxdex.Api.Data;
using Looxdex.Api.Models;
using Looxdex.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DailyLookController : ControllerBase
{
    private readonly LooxdexDbContext _db;
    private readonly LooxdexSeedData _data;
    private readonly IOwnerContext _owner;

    public DailyLookController(LooxdexDbContext db, LooxdexSeedData data, IOwnerContext owner)
    {
        _db = db;
        _data = data;
        _owner = owner;
    }

    [HttpGet]
    public async Task<ActionResult<DailyLook>> GetDailyLook(CancellationToken ct)
    {
        var weather = _data.CurrentWeather;
        var isWarm = weather.TempCelsius >= 20;

        // Build the look from whatever is actually in this owner's closet rather
        // than fixed ids, so it still works once they add their own pieces.
        var wanted = isWarm
            ? new[] { "חולצות", "מכנסיים", "נעליים", "תיקים" }
            : new[] { "חולצות", "מכנסיים", "נעליים", "תיקים" };

        var season = isWarm ? "קיץ" : "חורף";

        var closet = await _db.ClosetItems
            .AsNoTracking()
            .Where(i => i.OwnerKey == _owner.OwnerKey)
            .ToListAsync(ct);

        var items = wanted
            .Select(category => closet
                .Where(i => i.Category == category)
                .OrderByDescending(i => i.Season == season || i.Season == "כל השנה")
                .ThenByDescending(i => i.IsFavorite)
                .ThenByDescending(i => i.AddedAt)
                .FirstOrDefault())
            .Where(i => i is not null)
            .Select(i => new ClosetItem
            {
                Id = i!.Id,
                Name = i.Name,
                ImageUrl = i.ImageUrl,
                Category = i.Category,
                Color = i.Color,
                ColorHex = i.ColorHex,
                Season = i.Season,
                Brand = i.Brand,
                Formality = i.Formality,
                IsFavorite = i.IsFavorite,
                AddedAt = i.AddedAt
            })
            .ToList();

        var look = new DailyLook
        {
            Headline = isWarm ? "יום חמים וקליל בתל אביב" : "שכבות אלגנטיות ליום קריר",
            Reasoning = isWarm
                ? $"בהתאם למזג האוויר הנעים היום ({weather.TempCelsius}°C, {weather.City}), הרכבנו לוק קליל מבד טבעי לגמרי מהארון שלך."
                : $"מזג האוויר היום קריר יחסית ({weather.TempCelsius}°C, {weather.City}) — הנה לוק בשכבות שכולו כבר בארון שלך.",
            Weather = weather,
            Items = items
        };

        return Ok(look);
    }
}
