namespace Looxdex.Api.Services.Detection;

public record FashionLabel(string LabelHe, string Category);

/// <summary>
/// Maps Fashionpedia's 46 classes onto the app's Hebrew labels and closet categories.
///
/// Only wearable items are listed. Fashionpedia also emits garment *parts* —
/// sleeve, pocket, neckline, collar, lapel, zipper, buckle, and the decorative
/// classes (bead, sequin, ruffle...). Those are correct detections but useless as
/// "items in this look", so anything absent from this map is dropped.
/// </summary>
public static class FashionpediaLabels
{
    private static readonly Dictionary<string, FashionLabel> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- tops ---
        ["shirt, blouse"] = new("חולצה מכופתרת", "חולצות"),
        ["top, t-shirt, sweatshirt"] = new("טופ / חולצת טי", "חולצות"),
        ["sweater"] = new("סוודר", "חולצות"),
        ["cardigan"] = new("קרדיגן", "חולצות"),
        ["vest"] = new("וסט", "חולצות"),

        // --- outerwear ---
        ["jacket"] = new("ז'קט", "חולצות"),
        ["coat"] = new("מעיל", "חולצות"),
        ["cape"] = new("קייפ", "חולצות"),

        // --- bottoms ---
        ["pants"] = new("מכנסיים", "מכנסיים"),
        ["shorts"] = new("מכנסיים קצרים", "מכנסיים"),
        ["skirt"] = new("חצאית", "שמלות"),

        // --- one-piece ---
        ["dress"] = new("שמלה", "שמלות"),
        ["jumpsuit"] = new("אוברול", "שמלות"),

        // --- footwear & legwear ---
        ["shoe"] = new("נעליים", "נעליים"),
        ["sock"] = new("גרביים", "נעליים"),
        ["tights, stockings"] = new("גרביונים", "נעליים"),
        ["leg warmer"] = new("חממי רגליים", "נעליים"),

        // --- bags & accessories ---
        ["bag, wallet"] = new("תיק", "תיקים"),
        ["belt"] = new("חגורה", "תיקים"),
        ["scarf"] = new("צעיף", "תיקים"),
        ["glasses"] = new("משקפיים", "תיקים"),
        ["hat"] = new("כובע", "תיקים"),
        ["headband, head covering, hair accessory"] = new("אביזר שיער", "תיקים"),
        ["watch"] = new("שעון", "תיקים"),
        ["tie"] = new("עניבה", "תיקים"),
        ["glove"] = new("כפפות", "תיקים"),
        ["umbrella"] = new("מטרייה", "תיקים")
    };

    /// <summary>
    /// Class index order from the model's config.json id2label. Indices 0–26 are
    /// wearable items; 27–45 are parts and trims, which Resolve filters out.
    /// </summary>
    private static readonly string[] ByIndex =
    {
        "shirt, blouse",
        "top, t-shirt, sweatshirt",
        "sweater",
        "cardigan",
        "jacket",
        "vest",
        "pants",
        "shorts",
        "skirt",
        "coat",
        "dress",
        "jumpsuit",
        "cape",
        "glasses",
        "hat",
        "headband, head covering, hair accessory",
        "tie",
        "glove",
        "watch",
        "belt",
        "leg warmer",
        "tights, stockings",
        "sock",
        "shoe",
        "bag, wallet",
        "scarf",
        "umbrella",
        "hood",
        "collar",
        "lapel",
        "epaulette",
        "sleeve",
        "pocket",
        "neckline",
        "buckle",
        "zipper",
        "applique",
        "bead",
        "bow",
        "flower",
        "fringe",
        "ribbon",
        "rivet",
        "ruffle",
        "sequin",
        "tassel"
    };

    public static int ClassCount => ByIndex.Length;

    /// <summary>Raw Fashionpedia label for a class index, or null if out of range.</summary>
    public static string? LabelForIndex(int index) =>
        index >= 0 && index < ByIndex.Length ? ByIndex[index] : null;

    /// <summary>Returns the Hebrew label and category, or null if the class is a garment part.</summary>
    public static FashionLabel? Resolve(string? rawLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLabel)) return null;
        return Map.TryGetValue(rawLabel.Trim(), out var mapped) ? mapped : null;
    }

    public static bool IsWearable(string? rawLabel) => Resolve(rawLabel) is not null;
}
