namespace Looxdex.Api.Models;

public class ClosetItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#000000";
    public string Season { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Formality { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public class CreateClosetItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#000000";
    public string Season { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Formality { get; set; } = string.Empty;
}
