using Looxdex.Api.Models;
using Looxdex.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedController : ControllerBase
{
    private readonly FeedReadService _feed;

    public FeedController(FeedReadService feed)
    {
        _feed = feed;
    }

    /// <summary>
    /// One page of the feed. Pass the previous response's <c>nextCursor</c> to page
    /// forward; a null cursor in the response means the end of the library.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<FeedPage>> GetFeed(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        [FromQuery(Name = "q")] string? search,
        [FromQuery] bool saved = false,
        CancellationToken ct = default)
    {
        var page = await _feed.GetPageAsync(cursor, limit, search, saved, ct);
        return Ok(page);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FeedPost>> GetById(int id, CancellationToken ct)
    {
        var post = await _feed.GetByIdAsync(id, ct);
        return post is null ? NotFound() : Ok(post);
    }

    [HttpPatch("{id:int}/save")]
    public async Task<ActionResult<FeedPost>> ToggleSave(int id, CancellationToken ct)
    {
        var post = await _feed.ToggleSaveAsync(id, ct);
        return post is null ? NotFound() : Ok(post);
    }
}
