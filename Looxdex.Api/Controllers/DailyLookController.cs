using Looxdex.Api.Data;
using Looxdex.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DailyLookController : ControllerBase
{
    private readonly LooxdexSeedData _data;

    public DailyLookController(LooxdexSeedData data)
    {
        _data = data;
    }

    [HttpGet]
    public ActionResult<DailyLook> GetDailyLook()
    {
        var weather = _data.CurrentWeather;
        var isWarm = weather.TempCelsius >= 20;

        var idsForLook = isWarm ? new[] { 1, 2, 6, 7 } : new[] { 10, 5, 13, 12 };
        var items = _data.ClosetItems.Where(i => idsForLook.Contains(i.Id)).ToList();

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
