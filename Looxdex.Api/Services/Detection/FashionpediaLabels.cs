namespace Looxdex.Api.Services.Detection;

/// <summary>
/// Hebrew agreement class of a garment noun. An adjective has to match it, so
/// "מכנסיים" takes "כחולים" while "חולצה" takes "כחולה".
/// </summary>
public enum HebrewForm
{
    MasculineSingular,
    FeminineSingular,
    MasculinePlural,
    FemininePlural
}

/// <param name="LabelHe">What the overlay calls the item.</param>
/// <param name="Category">Closet category it files under.</param>
/// <param name="Form">Agreement class, for naming the item with its colour.</param>
/// <param name="DisplayHe">
/// Shorter name for a product tile, where the overlay label would read as a list.
/// Defaults to <paramref name="LabelHe"/>.
/// </param>
public record FashionLabel(
    string LabelHe,
    string Category,
    HebrewForm Form,
    string? DisplayHe = null)
{
    public string ProductName => DisplayHe ?? LabelHe;
}

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
        ["shirt, blouse"] = new("חולצה מכופתרת", "חולצות", HebrewForm.FeminineSingular),
        ["top, t-shirt, sweatshirt"] = new("טופ / חולצת טי", "חולצות", HebrewForm.MasculineSingular, "טופ"),
        ["sweater"] = new("סוודר", "חולצות", HebrewForm.MasculineSingular),
        ["cardigan"] = new("קרדיגן", "חולצות", HebrewForm.MasculineSingular),
        ["vest"] = new("וסט", "חולצות", HebrewForm.MasculineSingular),

        // --- outerwear ---
        ["jacket"] = new("ז'קט", "חולצות", HebrewForm.MasculineSingular),
        ["coat"] = new("מעיל", "חולצות", HebrewForm.MasculineSingular),
        ["cape"] = new("קייפ", "חולצות", HebrewForm.MasculineSingular),

        // --- bottoms ---
        ["pants"] = new("מכנסיים", "מכנסיים", HebrewForm.MasculinePlural),
        ["shorts"] = new("מכנסיים קצרים", "מכנסיים", HebrewForm.MasculinePlural),
        ["skirt"] = new("חצאית", "שמלות", HebrewForm.FeminineSingular),

        // --- one-piece ---
        ["dress"] = new("שמלה", "שמלות", HebrewForm.FeminineSingular),
        ["jumpsuit"] = new("אוברול", "שמלות", HebrewForm.MasculineSingular),

        // --- footwear & legwear ---
        ["shoe"] = new("נעליים", "נעליים", HebrewForm.FemininePlural),
        ["sock"] = new("גרביים", "נעליים", HebrewForm.MasculinePlural),
        ["tights, stockings"] = new("גרביונים", "נעליים", HebrewForm.MasculinePlural),
        ["leg warmer"] = new("חממי רגליים", "נעליים", HebrewForm.MasculinePlural),

        // --- bags & accessories ---
        ["bag, wallet"] = new("תיק", "תיקים", HebrewForm.MasculineSingular),
        ["belt"] = new("חגורה", "תיקים", HebrewForm.FeminineSingular),
        ["scarf"] = new("צעיף", "תיקים", HebrewForm.MasculineSingular),
        ["glasses"] = new("משקפיים", "תיקים", HebrewForm.MasculinePlural),
        ["hat"] = new("כובע", "תיקים", HebrewForm.MasculineSingular),
        ["headband, head covering, hair accessory"] =
            new("אביזר שיער", "תיקים", HebrewForm.MasculineSingular),
        ["watch"] = new("שעון", "תיקים", HebrewForm.MasculineSingular),
        ["tie"] = new("עניבה", "תיקים", HebrewForm.FeminineSingular),
        ["glove"] = new("כפפות", "תיקים", HebrewForm.FemininePlural),
        ["umbrella"] = new("מטרייה", "תיקים", HebrewForm.FeminineSingular)
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
