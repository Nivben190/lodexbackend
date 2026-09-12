using System.ComponentModel.DataAnnotations;

namespace Looxdex.Api.Entities;

/// <summary>
/// A photo uploaded from someone's device.
///
/// Bytes live in the database rather than object storage: it keeps the stack to
/// one dependency while the library is small, and the client downscales before
/// upload so rows stay in the low hundreds of KB. Moving to Supabase Storage
/// later only changes where <see cref="UploadedImageEntity"/> is read from.
/// </summary>
public class UploadedImageEntity
{
    /// <summary>Opaque id used in the public URL.</summary>
    [MaxLength(32)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(64)]
    public string OwnerKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = "image/jpeg";

    public byte[] Data { get; set; } = Array.Empty<byte>();

    public int ByteSize { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks what has been done once for an owner, so first-run work is not
/// repeated. Without it, emptying the closet would silently re-seed it.
/// </summary>
public class OwnerProfileEntity
{
    [MaxLength(64)]
    public string OwnerKey { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the starter closet was created; null if it never was.</summary>
    public DateTime? SeededAt { get; set; }
}
