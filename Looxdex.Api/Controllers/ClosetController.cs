using Looxdex.Api.Data;
using Looxdex.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClosetController : ControllerBase
{
    private readonly LooxdexSeedData _data;

    public ClosetController(LooxdexSeedData data)
    {
        _data = data;
    }

    [HttpGet]
    public ActionResult<IEnumerable<ClosetItem>> GetCloset(
        [FromQuery] string? category,
        [FromQuery] string? color,
        [FromQuery] string? season)
    {
        var items = _data.ClosetItems.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && category != "הכל")
            items = items.Where(i => i.Category == category);

        if (!string.IsNullOrWhiteSpace(color))
            items = items.Where(i => i.Color == color);

        if (!string.IsNullOrWhiteSpace(season))
            items = items.Where(i => i.Season == season);

        return Ok(items.OrderByDescending(i => i.AddedAt));
    }

    [HttpGet("{id:int}")]
    public ActionResult<ClosetItem> GetById(int id)
    {
        var item = _data.ClosetItems.FirstOrDefault(i => i.Id == id);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public ActionResult<ClosetItem> AddItem([FromBody] CreateClosetItemRequest request)
    {
        var item = new ClosetItem
        {
            Id = _data.GetNextClosetId(),
            Name = request.Name,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl)
                ? "https://images.unsplash.com/photo-1445205170230-053b83016050?w=400&q=80"
                : request.ImageUrl,
            Category = request.Category,
            Color = request.Color,
            ColorHex = request.ColorHex,
            Season = request.Season,
            Brand = request.Brand,
            Formality = request.Formality,
            AddedAt = DateTime.UtcNow
        };

        _data.ClosetItems.Insert(0, item);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpDelete("{id:int}")]
    public IActionResult DeleteItem(int id)
    {
        var item = _data.ClosetItems.FirstOrDefault(i => i.Id == id);
        if (item is null) return NotFound();
        _data.ClosetItems.Remove(item);
        return NoContent();
    }
}
