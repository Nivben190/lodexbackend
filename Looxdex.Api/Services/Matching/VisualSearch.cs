using System.Text.Json;
using Looxdex.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Matching;

/// <param name="Title">The shop's own name for the garment.</param>
/// <param name="Source">Which shop it is.</param>
/// <param name="Link">The product page.</param>
/// <param name="ThumbnailUrl">Google's thumbnail, linked rather than copied.</param>
public record VisualMatch(
    string Title,
    string Source,
    string Link,
    string ThumbnailUrl,
    decimal Price,
    string Currency);

public interface IVisualSearch
{
    bool Enabled { get; }

    /// <summary>
    /// Finds garments for sale that look like the picture at <paramref name="imageUrl"/>.
    /// The URL has to be reachable from the outside, since the search fetches it.
    /// </summary>
    Task<IReadOnlyList<VisualMatch>> FindAsync(string imageUrl, CancellationToken ct);
}

/// <summary>
/// Reverse image search against Google Lens, through SerpApi.
///
/// Google has never published a Lens API, so this goes through a service that
/// does the fetching and carries that arrangement with Google on our behalf. What
/// comes back is what a shopper would see: real shops, real prices, real links.
///
/// It is given the cut-out, never the original photograph. Handed a street photo,
/// Lens finds the outfit, the location or the wrong garment; handed a pair of
/// trousers alone on white, it looks for trousers. The cutting work is what makes
/// this worth doing.
/// </summary>
public class SerpApiVisualSearch : IVisualSearch
{
    private const string Endpoint = "https://serpapi.com/search";

    private readonly HttpClient _http;
    private readonly VisualSearchOptions _options;
    private readonly ILogger<SerpApiVisualSearch> _logger;

    public SerpApiVisualSearch(
        HttpClient http,
        IOptions<VisualSearchOptions> options,
        ILogger<SerpApiVisualSearch> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <summary>
    /// Places a garment cannot be bought. Lens is happy to answer with a moodboard
    /// on Instagram or a pin on Pinterest — often the best visual likeness on the
    /// page, and useless to someone who wants the thing.
    /// </summary>
    private static readonly string[] NotShops =
    {
        "instagram", "pinterest", "tumblr", "facebook", "twitter", "x.com",
        "reddit", "youtube", "tiktok", "blogspot", "wordpress", "wikipedia",
        "lookastic", "flickr"
    };

    public async Task<IReadOnlyList<VisualMatch>> FindAsync(string imageUrl, CancellationToken ct)
    {
        if (!Enabled) return Array.Empty<VisualMatch>();

        // Asked for visual matches explicitly: left to itself the endpoint now
        // answers with Google's AI summary of the picture and no matches at all.
        var query = $"{Endpoint}?engine=google_lens&type=visual_matches" +
                    $"&url={Uri.EscapeDataString(imageUrl)}" +
                    $"&api_key={Uri.EscapeDataString(_options.ApiKey)}";

        try
        {
            using var response = await _http.GetAsync(query, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Visual search failed: {Status} {Body}",
                    response.StatusCode, body.Length > 200 ? body[..200] : body);
                return Array.Empty<VisualMatch>();
            }

            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                _logger.LogWarning("Visual search refused: {Error}", error.GetString());
                return Array.Empty<VisualMatch>();
            }

            if (!document.RootElement.TryGetProperty("visual_matches", out var matches))
            {
                return Array.Empty<VisualMatch>();
            }

            var results = new List<VisualMatch>();

            foreach (var match in matches.EnumerateArray())
            {
                var link = Text(match, "link");
                var thumbnail = Text(match, "thumbnail");

                // Without somewhere to go and something to show, a result is noise.
                if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(thumbnail)) continue;

                var source = Text(match, "source");

                if (NotShops.Any(n =>
                        link.Contains(n, StringComparison.OrdinalIgnoreCase)
                        || source.Contains(n, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var (price, currency) = ReadPrice(match);

                results.Add(new VisualMatch(
                    Text(match, "title"),
                    source,
                    link,
                    thumbnail,
                    price,
                    currency));

                if (results.Count >= _options.MaxResults) break;
            }

            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Visual search errored for {Url}.", imageUrl);
            return Array.Empty<VisualMatch>();
        }
    }

    /// <summary>
    /// Price, when the shop published one. It arrives as an object carrying both
    /// the printed string and a number, and the number is the useful half.
    /// </summary>
    private static (decimal Price, string Currency) ReadPrice(JsonElement match)
    {
        if (!match.TryGetProperty("price", out var price) || price.ValueKind != JsonValueKind.Object)
        {
            return (0, string.Empty);
        }

        decimal value = 0;

        if (price.TryGetProperty("extracted_value", out var extracted)
            && extracted.ValueKind == JsonValueKind.Number)
        {
            value = extracted.GetDecimal();
        }

        var currency = price.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "";

        return (value, currency);
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
