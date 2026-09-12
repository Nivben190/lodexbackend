using Looxdex.Api.Configuration;
using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Ingest;

public record IngestResult(int Requested, int Added, int Duplicates, int Skipped);

/// <summary>
/// Pulls photos from the configured image source into our own feed table.
/// The app never calls the provider on a user request — that keeps the feed
/// fast, paginated from our own DB, and clear of provider rate limits.
/// </summary>
public class FeedIngestService
{
    private readonly LooxdexDbContext _db;
    private readonly IImageSource _source;
    private readonly PexelsOptions _options;
    private readonly ILogger<FeedIngestService> _logger;

    public FeedIngestService(
        LooxdexDbContext db,
        IImageSource source,
        IOptions<PexelsOptions> options,
        ILogger<FeedIngestService> logger)
    {
        _db = db;
        _source = source;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IngestResult> IngestAsync(
        string? query, int page, int? perPage, CancellationToken ct)
    {
        if (!_source.Enabled)
        {
            _logger.LogInformation("Ingest skipped: image source is not configured.");
            return new IngestResult(0, 0, 0, 0);
        }

        var total = await _db.FeedPosts.CountAsync(ct);
        if (total >= _options.MaxLibrarySize)
        {
            _logger.LogInformation(
                "Ingest skipped: library at {Total}/{Max} posts.", total, _options.MaxLibrarySize);
            return new IngestResult(0, 0, 0, 0);
        }

        var queries = string.IsNullOrWhiteSpace(query)
            ? _options.Queries
            : new List<string> { query };

        var requested = 0;
        var added = 0;
        var duplicates = 0;
        var skipped = 0;

        foreach (var q in queries)
        {
            ct.ThrowIfCancellationRequested();

            var candidates = await _source.SearchAsync(q, page, perPage ?? _options.PerPage, ct);
            requested += candidates.Count;
            if (candidates.Count == 0) continue;

            // One round trip to find which of this batch we already hold.
            var externalIds = candidates.Select(c => c.ExternalId).ToList();
            var existing = await _db.FeedPosts
                .Where(p => p.Source == FeedSource.Pexels && externalIds.Contains(p.ExternalId))
                .Select(p => p.ExternalId)
                .ToHashSetAsync(ct);

            var batch = new List<FeedPostEntity>();

            foreach (var candidate in candidates)
            {
                if (existing.Contains(candidate.ExternalId))
                {
                    duplicates++;
                    continue;
                }

                // Guard against the same photo appearing twice within one response.
                if (batch.Any(b => b.ExternalId == candidate.ExternalId))
                {
                    duplicates++;
                    continue;
                }

                if (candidate.Width <= 0 || candidate.Height <= 0)
                {
                    skipped++;
                    continue;
                }

                batch.Add(ToEntity(candidate));
            }

            if (batch.Count > 0)
            {
                _db.FeedPosts.AddRange(batch);
                await _db.SaveChangesAsync(ct);
                added += batch.Count;
            }

            _logger.LogInformation(
                "Ingested {Added} new posts for query {Query} ({Duplicates} duplicates).",
                batch.Count, q, duplicates);
        }

        return new IngestResult(requested, added, duplicates, skipped);
    }

    private static FeedPostEntity ToEntity(IngestCandidate c)
    {
        var (w, h) = NormaliseAspect(c.Width, c.Height);

        return new FeedPostEntity
        {
            Source = c.Source,
            ExternalId = c.ExternalId,
            ImageUrl = c.ImageUrl,
            ThumbnailUrl = c.ThumbnailUrl,
            Title = c.Title,
            Photographer = c.Photographer,
            PhotographerUrl = c.PhotographerUrl,
            SourceUrl = c.SourceUrl,
            Query = c.Query,
            Location = string.Empty,
            Likes = 0,
            AspectRatioWidth = w,
            AspectRatioHeight = h,
            DetectionState = DetectionState.Pending,
            IngestedAt = DateTime.UtcNow,
            Rank = DateTime.UtcNow.Ticks
        };
    }

    /// <summary>
    /// Reduce the real pixel dimensions to a small ratio. The masonry sets
    /// `aspect-ratio: w / h` before the image loads, so the grid never reflows.
    /// </summary>
    private static (int Width, int Height) NormaliseAspect(int width, int height)
    {
        var divisor = Gcd(width, height);
        if (divisor == 0) return (3, 4);

        var w = width / divisor;
        var h = height / divisor;

        // Keep the ratio sane so one extreme panorama cannot blow out a column.
        while (w > 20 || h > 20)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        return (w, h);
    }

    private static int Gcd(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
