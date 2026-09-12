using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Looxdex.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    /// <summary>
    /// Hard ceiling on a stored photo. The client downscales before sending, so
    /// anything near this is either an unusual camera or a bad actor.
    /// </summary>
    private const int MaxBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private readonly LooxdexDbContext _db;
    private readonly IOwnerContext _owner;
    private readonly ILogger<ImagesController> _logger;

    public ImagesController(LooxdexDbContext db, IOwnerContext owner, ILogger<ImagesController> logger)
    {
        _db = db;
        _owner = owner;
        _logger = logger;
    }

    /// <summary>Stores a photo from the wearer's device and returns its URL.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxBytes + 512 * 1024)]
    public async Task<ActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "לא נבחרה תמונה." });
        }

        if (file.Length > MaxBytes)
        {
            return BadRequest(new { error = "התמונה גדולה מדי." });
        }

        if (!AllowedTypes.Contains(file.ContentType))
        {
            return BadRequest(new { error = "סוג הקובץ אינו נתמך." });
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();

        // Trust the bytes, not the declared type: a renamed file would otherwise
        // be stored and served back under whatever content type the client claimed.
        var sniffed = SniffContentType(bytes);
        if (sniffed is null)
        {
            return BadRequest(new { error = "הקובץ אינו תמונה תקינה." });
        }

        var entity = new UploadedImageEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerKey = _owner.OwnerKey,
            ContentType = sniffed,
            Data = bytes,
            ByteSize = bytes.Length,
            CreatedAt = DateTime.UtcNow
        };

        _db.UploadedImages.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Stored upload {Id} ({Size:N0} bytes).", entity.Id, bytes.Length);

        // Absolute, because the client is served from a different origin than the
        // API — a relative path would resolve against the web host and 404.
        var url = $"{Request.Scheme}://{Request.Host}/api/images/{entity.Id}";

        return Ok(new { id = entity.Id, url });
    }

    /// <summary>
    /// Serves a stored photo. Deliberately unauthenticated: the id is an opaque
    /// GUID and the URL is embedded in closet items rendered by the browser.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var image = await _db.UploadedImages
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (image is null) return NotFound();

        // Content is immutable once written, so it can be cached hard.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return File(image.Data, image.ContentType);
    }

    /// <summary>Identifies a format from its magic bytes, or null if unrecognised.</summary>
    private static string? SniffContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12) return null;

        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";

        if (bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            return "image/png";

        if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return "image/webp";

        return null;
    }
}
