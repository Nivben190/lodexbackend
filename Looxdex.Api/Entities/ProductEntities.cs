using System.ComponentModel.DataAnnotations;

namespace Looxdex.Api.Entities;

/// <summary>
/// A garment in the shoppable catalogue: what a shop photographed against white,
/// which is what a detected item is matched against.
///
/// The picture is stored rather than linked. A catalogue built from someone
/// else's hosting is a catalogue that breaks quietly, and these are the images
/// the app shows in place of a cut-out, so they have to be there tomorrow.
/// </summary>
public class ProductEntity
{
    public int Id { get; set; }

    /// <summary>Id in the source catalogue, to avoid ingesting the same thing twice.</summary>
    [MaxLength(64)]
    public string ExternalId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Source { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Brand { get; set; } = string.Empty;

    /// <summary>The catalogue's own type, e.g. "Shirts", "Jeans", "Heels".</summary>
    [MaxLength(80)]
    public string ArticleType { get; set; } = string.Empty;

    [MaxLength(60)]
    public string Colour { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Gender { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>Id in <see cref="UploadedImageEntity"/> of the product photograph.</summary>
    [MaxLength(32)]
    public string ImageId { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string StoreUrl { get; set; } = string.Empty;

    /// <summary>CLIP embedding of the photograph; the thing matching actually compares.</summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// Which model produced that embedding. Vectors are only comparable with
    /// others made the same way, so this is what says whether a stored one is
    /// still worth anything.
    /// </summary>
    [MaxLength(80)]
    public string EmbeddedWith { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
