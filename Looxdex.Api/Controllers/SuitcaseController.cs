using Looxdex.Api.Data;
using Looxdex.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuitcaseController : ControllerBase
{
    private readonly LooxdexSeedData _data;

    public SuitcaseController(LooxdexSeedData data)
    {
        _data = data;
    }

    [HttpGet]
    public ActionResult<IEnumerable<Suitcase>> GetSuitcases()
    {
        return Ok(_data.Suitcases);
    }

    [HttpGet("{id:int}")]
    public ActionResult<Suitcase> GetById(int id)
    {
        var suitcase = _data.Suitcases.FirstOrDefault(s => s.Id == id);
        return suitcase is null ? NotFound() : Ok(suitcase);
    }

    [HttpPatch("toggle-packed")]
    public ActionResult<Suitcase> TogglePacked([FromBody] TogglePackedRequest request)
    {
        var suitcase = _data.Suitcases.FirstOrDefault(s => s.Id == request.SuitcaseId);
        if (suitcase is null) return NotFound();

        var item = suitcase.EventGroups
            .SelectMany(g => g.Items)
            .FirstOrDefault(i => i.Id == request.PackingItemId);

        if (item is null) return NotFound();

        item.IsPacked = request.IsPacked;
        return Ok(suitcase);
    }

    [HttpPost("{id:int}/add-look")]
    public ActionResult<AddLookToSuitcaseResult> AddLook(int id, [FromBody] AddLookToSuitcaseRequest request)
    {
        var suitcase = _data.Suitcases.FirstOrDefault(s => s.Id == id);
        if (suitcase is null) return NotFound();

        var post = _data.FeedPosts.FirstOrDefault(p => p.Id == request.PostId);
        if (post is null) return NotFound();

        var group = suitcase.EventGroups.FirstOrDefault(g => g.EventKey == request.EventKey);
        if (group is null) return NotFound();

        var ownedClosetIds = post.DetectedItems
            .Where(d => d.OwnedInCloset && d.MatchingClosetItemId.HasValue)
            .Select(d => d.MatchingClosetItemId!.Value)
            .Where(closetId => !group.Items.Any(i => i.ClosetItemId == closetId))
            .Distinct();

        var addedCount = 0;
        foreach (var closetId in ownedClosetIds)
        {
            var closetItem = _data.ClosetItems.FirstOrDefault(c => c.Id == closetId);
            if (closetItem is null) continue;

            group.Items.Add(new PackingItem
            {
                Id = _data.GetNextPackingItemId(),
                ClosetItemId = closetItem.Id,
                Name = closetItem.Name,
                ImageUrl = closetItem.ImageUrl,
                Category = closetItem.Category,
                IsPacked = false
            });
            addedCount++;
        }

        return Ok(new AddLookToSuitcaseResult { Suitcase = suitcase, AddedCount = addedCount });
    }
}
