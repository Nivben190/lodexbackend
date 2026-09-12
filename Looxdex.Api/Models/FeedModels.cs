namespace Looxdex.Api.Models;

public class BoundingBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class ShoppingAlternative
{
    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string StoreUrl { get; set; } = string.Empty;
}

public class DetectedItem
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string LabelHe { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public BoundingBox Box { get; set; } = new();
    public bool OwnedInCloset { get; set; }
    public int? MatchingClosetItemId { get; set; }
    public List<int> SimilarClosetItemIds { get; set; } = new();
    public List<ShoppingAlternative> Alternatives { get; set; } = new();
}

public class FeedPost
{
    public int Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Photographer { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int Likes { get; set; }
    public int AspectRatioHeight { get; set; } = 4;
    public int AspectRatioWidth { get; set; } = 3;
    public bool IsSaved { get; set; }
    public List<DetectedItem> DetectedItems { get; set; } = new();
}
