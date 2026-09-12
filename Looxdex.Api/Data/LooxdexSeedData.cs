using Looxdex.Api.Models;

namespace Looxdex.Api.Data;

/// <summary>
/// Single in-memory data store shared across the app (registered as a singleton).
/// Mock data only — swap for a real persistence layer without touching controllers.
/// </summary>
public class LooxdexSeedData
{
    public List<ClosetItem> ClosetItems { get; }
    public List<FeedPost> FeedPosts { get; }
    public List<Suitcase> Suitcases { get; }
    public WeatherInfo CurrentWeather { get; } = new()
    {
        TempCelsius = 24,
        City = "תל אביב",
        Condition = "בהיר",
        Icon = "☀️"
    };

    private int _nextClosetId;
    private int _nextPackingItemId;

    public LooxdexSeedData()
    {
        ClosetItems = BuildClosetItems();
        _nextClosetId = ClosetItems.Max(i => i.Id) + 1;
        FeedPosts = BuildFeedPosts();
        Suitcases = BuildSuitcases();
        _nextPackingItemId = Suitcases.SelectMany(s => s.EventGroups).SelectMany(g => g.Items).Max(i => i.Id) + 1;
    }

    public int GetNextClosetId() => _nextClosetId++;
    public int GetNextPackingItemId() => _nextPackingItemId++;

    private static List<ClosetItem> BuildClosetItems() => new()
    {
        new ClosetItem { Id = 1, Name = "חולצת פשתן לבנה", ImageUrl = "https://images.unsplash.com/photo-1596755094514-f87e34085b2c?w=400&q=80", Category = "חולצות", Color = "לבן", ColorHex = "#F7F5F0", Season = "קיץ", Brand = "Zara", Formality = "יומיומי", IsFavorite = true },
        new ClosetItem { Id = 2, Name = "מכנסי לינן בז'", ImageUrl = "https://images.unsplash.com/photo-1594633312681-425c7b97ccd1?w=400&q=80", Category = "מכנסיים", Color = "בז'", ColorHex = "#D8C9AE", Season = "קיץ", Brand = "Massimo Dutti", Formality = "יומיומי" },
        new ClosetItem { Id = 3, Name = "שמלת מידי שחורה", ImageUrl = "https://images.unsplash.com/photo-1595777457583-95e059d581b8?w=400&q=80", Category = "שמלות", Color = "שחור", ColorHex = "#1A1A1A", Season = "כל השנה", Brand = "COS", Formality = "ערב", IsFavorite = true },
        new ClosetItem { Id = 4, Name = "בלייזר בז' מחויט", ImageUrl = "https://images.unsplash.com/photo-1591047139829-d91aecb6caea?w=400&q=80", Category = "חולצות", Color = "בז'", ColorHex = "#C9B896", Season = "אביב", Brand = "Mango", Formality = "אלגנט" },
        new ClosetItem { Id = 5, Name = "ג'ינס מחויט כחול", ImageUrl = "https://images.unsplash.com/photo-1541099649105-f69ad21f3246?w=400&q=80", Category = "מכנסיים", Color = "כחול", ColorHex = "#3B5A7A", Season = "כל השנה", Brand = "Levi's", Formality = "יומיומי" },
        new ClosetItem { Id = 6, Name = "סנדלי עור שטוחים", ImageUrl = "https://images.unsplash.com/photo-1543163521-1bf539c55dd2?w=400&q=80", Category = "נעליים", Color = "חום", ColorHex = "#8B5E3C", Season = "קיץ", Brand = "Ancient Greek Sandals", Formality = "יומיומי" },
        new ClosetItem { Id = 7, Name = "תיק צד עור קרם", ImageUrl = "https://images.unsplash.com/photo-1584917865442-de89df76afd3?w=400&q=80", Category = "תיקים", Color = "קרם", ColorHex = "#EFE6D8", Season = "כל השנה", Brand = "Mansur Gavriel", Formality = "יומיומי", IsFavorite = true },
        new ClosetItem { Id = 8, Name = "נעלי עקב שחורות", ImageUrl = "https://images.unsplash.com/photo-1543163521-1bf539c55dd2?w=400&q=80", Category = "נעליים", Color = "שחור", ColorHex = "#1A1A1A", Season = "כל השנה", Brand = "Jimmy Choo", Formality = "ערב" },
        new ClosetItem { Id = 9, Name = "חולצת משי בורדו", ImageUrl = "https://images.unsplash.com/photo-1551048632-6d6f5d1ef1f0?w=400&q=80", Category = "חולצות", Color = "בורדו", ColorHex = "#812D48", Season = "סתיו", Brand = "Reformation", Formality = "אלגנט" },
        new ClosetItem { Id = 10, Name = "מעיל טרנץ' קלאסי", ImageUrl = "https://images.unsplash.com/photo-1520975954732-35dd22299614?w=400&q=80", Category = "חולצות", Color = "בז'", ColorHex = "#C9B896", Season = "סתיו", Brand = "Burberry", Formality = "אלגנט" },
        new ClosetItem { Id = 11, Name = "חצאית מכופתרת שמנת", ImageUrl = "https://images.unsplash.com/photo-1583496661160-fb5886a13d14?w=400&q=80", Category = "שמלות", Color = "שמנת", ColorHex = "#F0E9DC", Season = "אביב", Brand = "Sezane", Formality = "יומיומי" },
        new ClosetItem { Id = 12, Name = "תיק כתף מיני שחור", ImageUrl = "https://images.unsplash.com/photo-1591561954557-26941169b49e?w=400&q=80", Category = "תיקים", Color = "שחור", ColorHex = "#1A1A1A", Season = "כל השנה", Brand = "Staud", Formality = "ערב" },
        new ClosetItem { Id = 13, Name = "סניקרס לבנים מינימליסטיים", ImageUrl = "https://images.unsplash.com/photo-1560769629-975ec94e6a86?w=400&q=80", Category = "נעליים", Color = "לבן", ColorHex = "#F7F5F0", Season = "כל השנה", Brand = "Common Projects", Formality = "יומיומי" },
        new ClosetItem { Id = 14, Name = "טישרט כותנה שמנת", ImageUrl = "https://images.unsplash.com/photo-1521572163474-6864f9cf17ab?w=400&q=80", Category = "חולצות", Color = "שמנת", ColorHex = "#F0E9DC", Season = "כל השנה", Brand = "Uniqlo", Formality = "יומיומי" },
        new ClosetItem { Id = 15, Name = "מכנס טרנינג מחויט אפור", ImageUrl = "https://images.unsplash.com/photo-1548883354-94bcfe321cbb?w=400&q=80", Category = "מכנסיים", Color = "אפור", ColorHex = "#9A958D", Season = "כל השנה", Brand = "Loulou Studio", Formality = "יומיומי" },
        new ClosetItem { Id = 16, Name = "שמלת ערב סאטן יין", ImageUrl = "https://images.unsplash.com/photo-1566174053879-31528523f8ae?w=400&q=80", Category = "שמלות", Color = "בורדו", ColorHex = "#812D48", Season = "כל השנה", Brand = "Rat & Boa", Formality = "ערב", IsFavorite = true },
    };

    private static List<FeedPost> BuildFeedPosts() => new()
    {
        new FeedPost
        {
            Id = 1, Title = "מינימליזם עירוני בתל אביב", Photographer = "נועה שגיא", Location = "נמל תל אביב",
            ImageUrl = "https://images.unsplash.com/photo-1509631179647-0177331693ae?w=600&q=80", Likes = 842, AspectRatioWidth = 3, AspectRatioHeight = 4, IsSaved = true,
            DetectedItems = new()
            {
                new DetectedItem { Id = 101, Label = "Beige Blazer", LabelHe = "בלייזר בז'", Category = "חולצות", Box = new BoundingBox { X = 18, Y = 8, Width = 64, Height = 38 }, OwnedInCloset = true, MatchingClosetItemId = 4 },
                new DetectedItem { Id = 102, Label = "Wide Trousers", LabelHe = "מכנסיים רחבים", Category = "מכנסיים", Box = new BoundingBox { X = 22, Y = 46, Width = 56, Height = 40 }, OwnedInCloset = false, SimilarClosetItemIds = new() { 2 },
                    Alternatives = new() { new ShoppingAlternative { Id = 1, Brand = "Massimo Dutti", Name = "מכנסי פשתן רחבים", Price = 349, ImageUrl = "https://images.unsplash.com/photo-1594633312681-425c7b97ccd1?w=300&q=80", StoreUrl = "#" } } },
                new DetectedItem { Id = 103, Label = "Leather Tote", LabelHe = "תיק עור גדול", Category = "תיקים", Box = new BoundingBox { X = 60, Y = 50, Width = 30, Height = 22 }, OwnedInCloset = true, MatchingClosetItemId = 7 },
            }
        },
        new FeedPost
        {
            Id = 2, Title = "שיק פריזאי לסתיו", Photographer = "מיה לוין", Location = "פריז",
            ImageUrl = "https://images.unsplash.com/photo-1483985988355-763728e1935b?w=600&q=80", Likes = 1203, AspectRatioWidth = 3, AspectRatioHeight = 5,
            DetectedItems = new()
            {
                new DetectedItem { Id = 201, Label = "Trench Coat", LabelHe = "מעיל טרנץ'", Category = "חולצות", Box = new BoundingBox { X = 10, Y = 5, Width = 80, Height = 55 }, OwnedInCloset = true, MatchingClosetItemId = 10 },
                new DetectedItem { Id = 202, Label = "Black Boots", LabelHe = "מגפי עור שחורים", Category = "נעליים", Box = new BoundingBox { X = 30, Y = 78, Width = 35, Height = 18 }, OwnedInCloset = false,
                    Alternatives = new() { new ShoppingAlternative { Id = 2, Brand = "& Other Stories", Name = "מגפון עור שחור", Price = 599, ImageUrl = "https://images.unsplash.com/photo-1543163521-1bf539c55dd2?w=300&q=80", StoreUrl = "#" } } },
            }
        },
        new FeedPost
        {
            Id = 3, Title = "ערב קיץ באיביזה", Photographer = "רוני אשכנזי", Location = "איביזה",
            ImageUrl = "https://images.unsplash.com/photo-1515886657613-9f3515b0c78f?w=600&q=80", Likes = 2041, AspectRatioWidth = 4, AspectRatioHeight = 5,
            DetectedItems = new()
            {
                new DetectedItem { Id = 301, Label = "Satin Slip Dress", LabelHe = "שמלת סאטן", Category = "שמלות", Box = new BoundingBox { X = 25, Y = 15, Width = 50, Height = 65 }, OwnedInCloset = true, MatchingClosetItemId = 16 },
                new DetectedItem { Id = 302, Label = "Mini Bag", LabelHe = "תיק מיני", Category = "תיקים", Box = new BoundingBox { X = 62, Y = 55, Width = 20, Height = 15 }, OwnedInCloset = true, MatchingClosetItemId = 12 },
            }
        },
        new FeedPost
        {
            Id = 4, Title = "קז'ואל שבתי", Photographer = "טל ברק", Location = "נווה צדק",
            ImageUrl = "https://images.unsplash.com/photo-1529139574466-a303027c1d8b?w=600&q=80", Likes = 567, AspectRatioWidth = 4, AspectRatioHeight = 4, IsSaved = true,
            DetectedItems = new()
            {
                new DetectedItem { Id = 401, Label = "Linen Shirt", LabelHe = "חולצת פשתן", Category = "חולצות", Box = new BoundingBox { X = 15, Y = 10, Width = 70, Height = 40 }, OwnedInCloset = true, MatchingClosetItemId = 1 },
                new DetectedItem { Id = 402, Label = "Denim Jeans", LabelHe = "ג'ינס", Category = "מכנסיים", Box = new BoundingBox { X = 20, Y = 48, Width = 60, Height = 42 }, OwnedInCloset = true, MatchingClosetItemId = 5 },
                new DetectedItem { Id = 403, Label = "Flat Sandals", LabelHe = "סנדלים שטוחים", Category = "נעליים", Box = new BoundingBox { X = 30, Y = 86, Width = 35, Height = 12 }, OwnedInCloset = true, MatchingClosetItemId = 6 },
            }
        },
        new FeedPost
        {
            Id = 5, Title = "אלגנטיות משרדית", Photographer = "דנה כהן", Location = "מגדלי עזריאלי",
            ImageUrl = "https://images.unsplash.com/photo-1490481651871-ab68de25d43d?w=600&q=80", Likes = 734, AspectRatioWidth = 3, AspectRatioHeight = 4,
            DetectedItems = new()
            {
                new DetectedItem { Id = 501, Label = "Silk Blouse", LabelHe = "חולצת משי", Category = "חולצות", Box = new BoundingBox { X = 20, Y = 12, Width = 60, Height = 35 }, OwnedInCloset = true, MatchingClosetItemId = 9 },
                new DetectedItem { Id = 502, Label = "Tailored Trousers", LabelHe = "מכנסיים מחויטים", Category = "מכנסיים", Box = new BoundingBox { X = 22, Y = 48, Width = 56, Height = 42 }, OwnedInCloset = false, SimilarClosetItemIds = new() { 5 },
                    Alternatives = new() { new ShoppingAlternative { Id = 3, Brand = "COS", Name = "מכנסיים מחויטים אפורים", Price = 429, ImageUrl = "https://images.unsplash.com/photo-1548883354-94bcfe321cbb?w=300&q=80", StoreUrl = "#" } } },
            }
        },
        new FeedPost
        {
            Id = 6, Title = "סוף שבוע בציריך", Photographer = "עדי רגב", Location = "ציריך",
            ImageUrl = "https://images.unsplash.com/photo-1483985988355-763728e1935b?w=600&q=80", Likes = 998, AspectRatioWidth = 3, AspectRatioHeight = 5,
            DetectedItems = new()
            {
                new DetectedItem { Id = 601, Label = "Wool Coat", LabelHe = "מעיל צמר", Category = "חולצות", Box = new BoundingBox { X = 12, Y = 8, Width = 76, Height = 60 }, OwnedInCloset = false,
                    Alternatives = new() { new ShoppingAlternative { Id = 4, Brand = "Arket", Name = "מעיל צמר ארוך", Price = 890, ImageUrl = "https://images.unsplash.com/photo-1520975954732-35dd22299614?w=300&q=80", StoreUrl = "#" } } },
                new DetectedItem { Id = 602, Label = "White Sneakers", LabelHe = "סניקרס לבנים", Category = "נעליים", Box = new BoundingBox { X = 32, Y = 82, Width = 34, Height = 14 }, OwnedInCloset = true, MatchingClosetItemId = 13 },
            }
        },
    };

    private static List<Suitcase> BuildSuitcases() => new()
    {
        new Suitcase
        {
            Id = 1, TripName = "סופ״ש בציריך", Destination = "ציריך, שווייץ",
            CoverImageUrl = "https://images.unsplash.com/photo-1515488764276-beab7607c1e6?w=600&q=80",
            StartDate = new DateTime(2026, 10, 2), EndDate = new DateTime(2026, 10, 5),
            ExpectedTempLow = 8, ExpectedTempHigh = 16,
            EventGroups = new()
            {
                new OutfitEventGroup { EventKey = "flight", EventLabel = "טיסה", Icon = "✈️", Items = new()
                {
                    new PackingItem { Id = 1, ClosetItemId = 5, Name = "ג'ינס מחויט כחול", ImageUrl = "https://images.unsplash.com/photo-1541099649105-f69ad21f3246?w=300&q=80", Category = "מכנסיים", IsPacked = true },
                    new PackingItem { Id = 2, ClosetItemId = 14, Name = "טישרט כותנה שמנת", ImageUrl = "https://images.unsplash.com/photo-1521572163474-6864f9cf17ab?w=300&q=80", Category = "חולצות", IsPacked = true },
                    new PackingItem { Id = 3, ClosetItemId = 13, Name = "סניקרס לבנים מינימליסטיים", ImageUrl = "https://images.unsplash.com/photo-1560769629-975ec94e6a86?w=300&q=80", Category = "נעליים", IsPacked = false },
                }},
                new OutfitEventGroup { EventKey = "day", EventLabel = "יום / סייטסיינג", Icon = "🚶‍♀️", Items = new()
                {
                    new PackingItem { Id = 4, ClosetItemId = 10, Name = "מעיל טרנץ' קלאסי", ImageUrl = "https://images.unsplash.com/photo-1520975954732-35dd22299614?w=300&q=80", Category = "חולצות", IsPacked = true },
                    new PackingItem { Id = 5, ClosetItemId = 15, Name = "מכנס טרנינג מחויט אפור", ImageUrl = "https://images.unsplash.com/photo-1548883354-94bcfe321cbb?w=300&q=80", Category = "מכנסיים", IsPacked = false },
                    new PackingItem { Id = 6, ClosetItemId = 13, Name = "סניקרס לבנים מינימליסטיים", ImageUrl = "https://images.unsplash.com/photo-1560769629-975ec94e6a86?w=300&q=80", Category = "נעליים", IsPacked = false },
                    new PackingItem { Id = 7, ClosetItemId = 7, Name = "תיק צד עור קרם", ImageUrl = "https://images.unsplash.com/photo-1584917865442-de89df76afd3?w=300&q=80", Category = "תיקים", IsPacked = true },
                }},
                new OutfitEventGroup { EventKey = "evening", EventLabel = "ערב / מסעדה", Icon = "🍷", Items = new()
                {
                    new PackingItem { Id = 8, ClosetItemId = 9, Name = "חולצת משי בורדו", ImageUrl = "https://images.unsplash.com/photo-1551048632-6d6f5d1ef1f0?w=300&q=80", Category = "חולצות", IsPacked = false },
                    new PackingItem { Id = 9, ClosetItemId = 3, Name = "שמלת מידי שחורה", ImageUrl = "https://images.unsplash.com/photo-1595777457583-95e059d581b8?w=300&q=80", Category = "שמלות", IsPacked = false },
                    new PackingItem { Id = 10, ClosetItemId = 8, Name = "נעלי עקב שחורות", ImageUrl = "https://images.unsplash.com/photo-1543163521-1bf539c55dd2?w=300&q=80", Category = "נעליים", IsPacked = false },
                }},
            }
        },
        new Suitcase
        {
            Id = 2, TripName = "חופשה באילת", Destination = "אילת, ישראל",
            CoverImageUrl = "https://images.unsplash.com/photo-1509233725247-49e657c54213?w=600&q=80",
            StartDate = new DateTime(2026, 11, 20), EndDate = new DateTime(2026, 11, 24),
            ExpectedTempLow = 21, ExpectedTempHigh = 29,
            EventGroups = new()
            {
                new OutfitEventGroup { EventKey = "flight", EventLabel = "טיסה", Icon = "✈️", Items = new()
                {
                    new PackingItem { Id = 11, ClosetItemId = 2, Name = "מכנסי לינן בז'", ImageUrl = "https://images.unsplash.com/photo-1594633312681-425c7b97ccd1?w=300&q=80", Category = "מכנסיים", IsPacked = true },
                    new PackingItem { Id = 12, ClosetItemId = 1, Name = "חולצת פשתן לבנה", ImageUrl = "https://images.unsplash.com/photo-1596755094514-f87e34085b2c?w=300&q=80", Category = "חולצות", IsPacked = true },
                }},
                new OutfitEventGroup { EventKey = "day", EventLabel = "יום / בריכה", Icon = "🏖️", Items = new()
                {
                    new PackingItem { Id = 13, ClosetItemId = 6, Name = "סנדלי עור שטוחים", ImageUrl = "https://images.unsplash.com/photo-1543163521-1bf539c55dd2?w=300&q=80", Category = "נעליים", IsPacked = false },
                    new PackingItem { Id = 14, ClosetItemId = 11, Name = "חצאית מכופתרת שמנת", ImageUrl = "https://images.unsplash.com/photo-1583496661160-fb5886a13d14?w=300&q=80", Category = "שמלות", IsPacked = false },
                }},
                new OutfitEventGroup { EventKey = "evening", EventLabel = "ערב / מסעדה", Icon = "🍷", Items = new()
                {
                    new PackingItem { Id = 15, ClosetItemId = 16, Name = "שמלת ערב סאטן יין", ImageUrl = "https://images.unsplash.com/photo-1566174053879-31528523f8ae?w=300&q=80", Category = "שמלות", IsPacked = false },
                    new PackingItem { Id = 16, ClosetItemId = 12, Name = "תיק כתף מיני שחור", ImageUrl = "https://images.unsplash.com/photo-1591561954557-26941169b49e?w=300&q=80", Category = "תיקים", IsPacked = true },
                }},
            }
        },
    };
}
