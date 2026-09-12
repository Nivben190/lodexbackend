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
        [FromQuery] string? folder = null,
        CancellationToken ct = default)
    {
        var page = await _feed.GetPageAsync(cursor, limit, search, saved, folder, ct);
        return Ok(page);
    }

    /// <summary>Folders the owner has filed saved looks under.</summary>
    [HttpGet("folders")]
    public async Task<ActionResult<IEnumerable<SavedFolder>>> GetFolders(CancellationToken ct)
    {
        return Ok(await _feed.GetFoldersAsync(ct));
    }

    /// <summary>Files a saved look under a folder. Saves it first if needed.</summary>
    [HttpPatch("{id:int}/folder")]
    public async Task<IActionResult> SetFolder(
        int id, [FromBody] SetFolderRequest request, CancellationToken ct)
    {
        var ok = await _feed.SetFolderAsync(id, request.Folder, ct);
        return ok ? NoContent() : NotFound();
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
