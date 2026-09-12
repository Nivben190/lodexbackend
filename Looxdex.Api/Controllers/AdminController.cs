using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Services.Detection;
using Looxdex.Api.Services.Ingest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Controllers;

/// <summary>
/// Operational endpoints for running the pipeline on demand.
///
/// NOTE: unauthenticated for now. These must be put behind admin auth in phase 1
/// before the API is exposed to real traffic.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly FeedIngestService _ingest;
    private readonly DetectionWorker _detection;
    private readonly LooxdexDbContext _db;

    public AdminController(
        FeedIngestService ingest,
        IEnumerable<IHostedService> hostedServices,
        LooxdexDbContext db)
    {
        _ingest = ingest;
        _detection = hostedServices.OfType<DetectionWorker>().First();
        _db = db;
    }

    /// <summary>Pulls a page of photos from the image provider into the library.</summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<IngestResult>> Ingest(
        [FromQuery] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int? perPage = null,
        CancellationToken ct = default)
    {
        var result = await _ingest.IngestAsync(query, page, perPage, ct);
        return Ok(result);
    }

    /// <summary>Runs one detection batch immediately rather than waiting for the timer.</summary>
    [HttpPost("detect")]
    public async Task<ActionResult> Detect(CancellationToken ct)
    {
        var processed = await _detection.RunSweepAsync(ct);
        return Ok(new { processed });
    }

    /// <summary>
    /// Puts failed posts back in the queue and clears their attempt count.
    /// Use after fixing whatever made detection fail, so the backlog is retried.
    /// </summary>
    [HttpPost("reset-failed")]
    public async Task<ActionResult> ResetFailed(CancellationToken ct)
    {
        var reset = await _db.FeedPosts
            .Where(p => p.DetectionState == DetectionState.Failed
                        || p.DetectionState == DetectionState.Skipped)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.DetectionState, DetectionState.Pending)
                      .SetProperty(p => p.DetectionAttempts, 0),
                ct);

        return Ok(new { reset });
    }

    /// <summary>Pipeline counters, handy while the library is filling up.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult> Stats(CancellationToken ct)
    {
        var byState = await _db.FeedPosts
            .GroupBy(p => p.DetectionState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Ok(new
        {
            totalPosts = await _db.FeedPosts.CountAsync(ct),
            detectedItems = await _db.DetectedItems.CountAsync(ct),
            closetItems = await _db.ClosetItems.CountAsync(ct),
            savedPosts = await _db.SavedPosts.CountAsync(ct),
            pending = byState.FirstOrDefault(s => s.State == DetectionState.Pending)?.Count ?? 0,
            completed = byState.FirstOrDefault(s => s.State == DetectionState.Completed)?.Count ?? 0,
            failed = byState.FirstOrDefault(s => s.State == DetectionState.Failed)?.Count ?? 0,
            skipped = byState.FirstOrDefault(s => s.State == DetectionState.Skipped)?.Count ?? 0
        });
    }
}
