using Looxdex.Api.Configuration;
using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Services.Detection;
using Looxdex.Api.Services.Ingest;
using Looxdex.Api.Services.Matching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private readonly ProductCatalogue _catalogue;
    private readonly ProductMatcher _matcher;
    private readonly OnnxDetectionOptions _detectionOptions;

    public AdminController(
        FeedIngestService ingest,
        IEnumerable<IHostedService> hostedServices,
        LooxdexDbContext db,
        ProductCatalogue catalogue,
        ProductMatcher matcher,
        IOptions<OnnxDetectionOptions> detectionOptions)
    {
        _ingest = ingest;
        _detection = hostedServices.OfType<DetectionWorker>().First();
        _db = db;
        _catalogue = catalogue;
        _matcher = matcher;
        _detectionOptions = detectionOptions.Value;
    }

    /// <summary>
    /// Adds one garment to the shoppable catalogue: fetches its photograph, learns
    /// what it looks like, and files it. Whatever the source — a dataset now, an
    /// affiliate feed later — this is the door it comes in through.
    /// </summary>
    [HttpPost("products")]
    public async Task<ActionResult> AddProduct(
        [FromBody] ProductImport product, CancellationToken ct)
    {
        var added = await _catalogue.AddAsync(product, ct);
        return added ? Ok(new { added = true }) : BadRequest(new { added = false });
    }

    /// <summary>Matches detected garments against the catalogue, a batch at a time.</summary>
    [HttpPost("match")]
    public async Task<ActionResult> Match(
        [FromQuery] int batchSize = 25,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        var (examined, matched) = await _matcher.RunAsync(batchSize, force, ct);
        return Ok(new { examined, matched });
    }

    /// <summary>How full the catalogue is, by category.</summary>
    [HttpGet("catalogue")]
    public async Task<ActionResult> Catalogue(CancellationToken ct)
    {
        var byType = await _db.Products
            .GroupBy(p => p.ArticleType)
            .Select(g => new { type = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToListAsync(ct);

        return Ok(new { total = byType.Sum(t => t.count), byType });
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
    /// Renders product cutouts for items detected before the cutout stage existed.
    /// Call repeatedly until it reports zero: one call handles one batch.
    /// </summary>
    [HttpPost("rebuild-cutouts")]
    public async Task<ActionResult> RebuildCutouts(
        [FromQuery] int? batchSize,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        var (posts, items) = await _detection.RebuildCutoutsAsync(batchSize, force, ct);
        return Ok(new { posts, items });
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

    /// <summary>
    /// Deletes stored detections that fall below the current confidence floor.
    /// Run after raising MinScore so already-analysed posts are cleaned up too,
    /// rather than only new ones benefiting from the stricter threshold.
    /// </summary>
    [HttpPost("prune-detections")]
    public async Task<ActionResult> PruneDetections(
        [FromQuery] double? minScore, CancellationToken ct = default)
    {
        var floor = minScore ?? _detectionOptions.MinScore;

        var removed = await _db.DetectedItems
            .Where(d => d.Score < floor)
            .ExecuteDeleteAsync(ct);

        // A post left with nothing is worth another pass under the new rules.
        var emptied = await _db.FeedPosts
            .Where(p => p.DetectionState == DetectionState.Completed && !p.DetectedItems.Any())
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.DetectionState, DetectionState.Skipped),
                ct);

        return Ok(new { floor, removed, postsNowEmpty = emptied });
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
