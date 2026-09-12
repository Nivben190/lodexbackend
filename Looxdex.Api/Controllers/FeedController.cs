using Looxdex.Api.Data;
using Looxdex.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedController : ControllerBase
{
    private readonly LooxdexSeedData _data;

    public FeedController(LooxdexSeedData data)
    {
        _data = data;
    }

    [HttpGet]
    public ActionResult<IEnumerable<FeedPost>> GetFeed()
    {
        return Ok(_data.FeedPosts);
    }

    [HttpGet("{id:int}")]
    public ActionResult<FeedPost> GetById(int id)
    {
        var post = _data.FeedPosts.FirstOrDefault(p => p.Id == id);
        return post is null ? NotFound() : Ok(post);
    }

    [HttpPatch("{id:int}/save")]
    public ActionResult<FeedPost> ToggleSave(int id)
    {
        var post = _data.FeedPosts.FirstOrDefault(p => p.Id == id);
        if (post is null) return NotFound();

        post.IsSaved = !post.IsSaved;
        return Ok(post);
    }
}
