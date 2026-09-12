using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Models;
using Looxdex.Api.Services;
using Looxdex.Api.Services.Detection;
using Looxdex.Api.Services.Ingest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Controllers;

/// <summary>
/// Bringing a look the wearer found elsewhere into her own feed.
///
/// The image comes from her — a screenshot, a camera roll photo, a picture a
/// friend sent — and that is what gets analysed. The link is separate, and only
/// ever used to credit the post it came from through Instagram's own embed. We
/// never fetch the photo from Instagram ourselves: the embed endpoint does not
/// return one, and going around it would be both scraping and hotlinking.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LooksController : ControllerBase
{
    private readonly LooxdexDbContext _db;
    private readonly InstagramOEmbedService _instagram;
    private readonly FeedReadService _feed;
    private readonly ILogger<LooksController> _logger;

    public LooksController(
        LooxdexDbContext db,
        InstagramOEmbedService instagram,
        FeedReadService feed,
        ILogger<LooksController> logger)
    {
        _db = db;
        _instagram = instagram;
        _feed = feed;
        _logger = logger;
    }

    /// <summary>
    /// Files a look: stores the image, resolves the source post if one was given,
    /// and queues the look for detection. Returns the post as the feed will show it.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(StoredImages.MaxBytes + 512 * 1024)]
    public async Task<ActionResult<FeedPost>> Import(
        IFormFile? file,
        [FromForm] string? sourceUrl,
        [FromForm] string? title,
        [FromForm] string? credit,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "לא נבחרה תמונה." });
        }

        if (file.Length > StoredImages.MaxBytes)
        {
            return BadRequest(new { error = "התמונה גדולה מדי." });
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();

        var contentType = StoredImages.SniffContentType(bytes);
        if (contentType is null)
        {
            return BadRequest(new { error = "הקובץ אינו תמונה תקינה." });
        }

        // The app's own uploader downscales before sending; an import has no such
        // manners — these arrive straight off a camera roll at 3000px and upward,
        // which is megabytes per row and a slow feed for no visible gain.
        (bytes, contentType) = StoredImages.Downscale(bytes, contentType);

        var embed = string.IsNullOrWhiteSpace(sourceUrl)
            ? null
            : await _instagram.ResolveAsync(sourceUrl, ct);

        if (!string.IsNullOrWhiteSpace(sourceUrl) && embed is null)
        {
            // Worth failing loudly: a private or deleted post cannot be credited,
            // and filing the look without a source quietly is how a photo ends up
            // in the feed with no way back to whoever made it.
            return BadRequest(new
            {
                error = "לא ניתן להטמיע את הפוסט הזה — ייתכן שהוא פרטי או נמחק."
            });
        }

        var source = embed is null ? FeedSource.UserUpload : FeedSource.Instagram;

        // Keyed on the image, not on the post. A carousel is one post holding ten
        // different outfits, and each of those is a look in its own right — but the
        // same photo sent twice is still the same look.
        var fingerprint = Fingerprint(bytes);
        var externalId = embed is null ? fingerprint : $"{embed.Shortcode}-{fingerprint}";

        var duplicate = await _db.FeedPosts
            .FirstOrDefaultAsync(p => p.Source == source && p.ExternalId == externalId, ct);

        if (duplicate is not null)
        {
            var existing = await _feed.GetByIdAsync(duplicate.Id, ct);
            return existing is null ? Conflict() : Ok(existing);
        }

        var imageId = Guid.NewGuid().ToString("N");

        _db.UploadedImages.Add(new UploadedImageEntity
        {
            Id = imageId,
            OwnerKey = "system:look",
            ContentType = contentType,
            Data = bytes,
            ByteSize = bytes.Length,
            CreatedAt = DateTime.UtcNow
        });

        var size = ImageDimensionReader.TryRead(bytes);
        var (aspectWidth, aspectHeight) = NormaliseAspect(size?.Width ?? 3, size?.Height ?? 4);

        // Absolute, and built from this request: the client is served from another
        // origin, and the same row is read by both localhost and the deployed API.
        var imageUrl = $"{Request.Scheme}://{Request.Host}/api/images/{imageId}";

        var post = new FeedPostEntity
        {
            Source = source,
            ExternalId = externalId,
            ImageUrl = imageUrl,
            ThumbnailUrl = imageUrl,
            Title = title?.Trim() ?? string.Empty,
            Photographer = credit?.Trim() ?? string.Empty,
            PhotographerUrl = embed?.Permalink ?? string.Empty,
            SourceUrl = embed?.Permalink ?? string.Empty,
            EmbedHtml = embed?.Html,
            Location = string.Empty,
            Query = string.Empty,
            Likes = 0,
            AspectRatioWidth = aspectWidth,
            AspectRatioHeight = aspectHeight,
            DetectionState = DetectionState.Pending,
            IngestedAt = DateTime.UtcNow,
            Rank = DateTime.UtcNow.Ticks
        };

        _db.FeedPosts.Add(post);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Imported look {Id} from {Source} ({Size:N0} bytes).", post.Id, source, bytes.Length);

        var dto = await _feed.GetByIdAsync(post.Id, ct);
        return dto is null ? StatusCode(500) : Ok(dto);
    }

    /// <summary>Short, stable id for an image's contents.</summary>
    private static string Fingerprint(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    /// Reduces real pixel dimensions to a small ratio, so the masonry can reserve
    /// the right space before the image loads. Mirrors the ingest service.
    /// </summary>
    private static (int Width, int Height) NormaliseAspect(int width, int height)
    {
        var divisor = Gcd(width, height);
        if (divisor == 0) return (3, 4);

        var w = width / divisor;
        var h = height / divisor;

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
