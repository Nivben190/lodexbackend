namespace Looxdex.Api.Models;

public class Outfit
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Background { get; set; } = "#F2F0ED";

    /// <summary>Flattened picture of the look, for the thumbnail. Null until one is saved.</summary>
    public string? PreviewUrl { get; set; }

    /// <summary>The layers, exactly as the canvas wrote them.</summary>
    public string Composition { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveOutfitRequest
{
    public string? Name { get; set; }
    public string? Background { get; set; }

    /// <summary>Id of an already-uploaded flattened preview, if one was made.</summary>
    public string? PreviewImageId { get; set; }

    public string Composition { get; set; } = "{}";
}
