using System.ComponentModel.DataAnnotations;

namespace Looxdex.Api.Entities;

/// <summary>
/// A look the wearer assembled herself: garments from her closet arranged on a
/// canvas, over a background, with whatever she wrote on it.
///
/// The arrangement is kept as JSON rather than as a table of layers. It is a
/// drawing, read and written whole by one screen, and nothing else queries inside
/// it — a layers table would buy joins nobody needs and a migration every time the
/// canvas learns a new trick.
/// </summary>
public class OutfitEntity
{
    public int Id { get; set; }

    /// <summary>Anonymous device id today; becomes the authenticated user id in phase 1.</summary>
    [MaxLength(64)]
    public string OwnerKey { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Canvas background, as a CSS colour.</summary>
    [MaxLength(32)]
    public string Background { get; set; } = "#F2F0ED";

    /// <summary>
    /// Flattened picture of the look, stored like any other image and used as the
    /// thumbnail. Composed in the browser, where the canvas already is.
    /// </summary>
    [MaxLength(32)]
    public string? PreviewImageId { get; set; }

    /// <summary>The layers, as the canvas writes them.</summary>
    public string Composition { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
