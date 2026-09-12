using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Looxdex.Api.Services;

/// <summary>
/// The rules every photo that enters the database has to pass, in one place so an
/// upload and a look import cannot drift apart on what they accept.
/// </summary>
public static class StoredImages
{
    /// <summary>
    /// Hard ceiling on a stored photo. The client downscales before sending, so
    /// anything near this is either an unusual camera or a bad actor.
    /// </summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Longest edge a stored photo needs.
    ///
    /// The feed never shows a look wider than a phone, but the cutouts are taken
    /// from a fraction of the frame — a pair of trousers is a quarter of it — and
    /// that fraction has to survive being shown at tile size. Cutting the photo to
    /// 1600 was cutting the tiles to a blur.
    /// </summary>
    public const int MaxEdge = 2400;

    /// <summary>
    /// Shrinks an oversized photo, returning the bytes to store and their type.
    ///
    /// Returns the original untouched when it is already small enough or cannot be
    /// decoded: a photo that will not open here is the detector's problem to report,
    /// not something to reject at the door.
    /// </summary>
    public static (byte[] Bytes, string ContentType) Downscale(byte[] bytes, string contentType)
    {
        try
        {
            using var image = Image.Load(bytes);

            var longest = Math.Max(image.Width, image.Height);
            if (longest <= MaxEdge) return (bytes, contentType);

            var scale = MaxEdge / (double)longest;

            image.Mutate(c => c.Resize(
                (int)Math.Round(image.Width * scale),
                (int)Math.Round(image.Height * scale),
                KnownResamplers.Lanczos3));

            using var buffer = new MemoryStream();
            image.Save(buffer, new JpegEncoder { Quality = 90 });

            return (buffer.ToArray(), "image/jpeg");
        }
        catch (Exception)
        {
            return (bytes, contentType);
        }
    }

    /// <summary>Identifies a format from its magic bytes, or null if unrecognised.</summary>
    public static string? SniffContentType(ReadOnlySpan<byte> bytes)
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
