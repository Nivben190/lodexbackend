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
