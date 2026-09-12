using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Looxdex.Api.Configuration;
using Looxdex.Api.Entities;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Ingest;

/// <summary>One photo from an upstream provider, normalised for ingest.</summary>
public record IngestCandidate(
    FeedSource Source,
    string ExternalId,
    string ImageUrl,
    string ThumbnailUrl,
    string Title,
    string Photographer,
    string PhotographerUrl,
    string SourceUrl,
    int Width,
    int Height,
    string Query);

public interface IImageSource
{
    bool Enabled { get; }
    Task<IReadOnlyList<IngestCandidate>> SearchAsync(string query, int page, int perPage, CancellationToken ct);
}

/// <summary>
/// Pexels search client. The free tier allows 200 requests/hour and 20k/month, which is
/// ample because ingest runs on a timer rather than per user request.
/// </summary>
public class PexelsImageSource : IImageSource
{
    private readonly HttpClient _http;
    private readonly PexelsOptions _options;
    private readonly ILogger<PexelsImageSource> _logger;

    public PexelsImageSource(
        HttpClient http,
        IOptions<PexelsOptions> options,
        ILogger<PexelsImageSource> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyList<IngestCandidate>> SearchAsync(
        string query, int page, int perPage, CancellationToken ct)
    {
        if (!Enabled)
        {
            _logger.LogDebug("Pexels ingest skipped: no API key configured.");
            return Array.Empty<IngestCandidate>();
        }

        var url = $"https://api.pexels.com/v1/search" +
                  $"?query={Uri.EscapeDataString(query)}" +
                  $"&per_page={Math.Clamp(perPage, 1, 80)}" +
                  $"&page={Math.Max(page, 1)}" +
                  $"&orientation=portrait";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(_options.ApiKey);

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Pexels search for {Query} failed with {Status}.", query, response.StatusCode);
            return Array.Empty<IngestCandidate>();
        }

        var payload = await response.Content.ReadFromJsonAsync<PexelsSearchResponse>(ct);
        if (payload?.Photos is null) return Array.Empty<IngestCandidate>();

        return payload.Photos
            .Where(p => p.Src is not null && !string.IsNullOrWhiteSpace(p.Src.Large))
            .Select(p => new IngestCandidate(
                Source: FeedSource.Pexels,
                ExternalId: p.Id.ToString(),
                ImageUrl: p.Src!.Large2x ?? p.Src.Large!,
                ThumbnailUrl: p.Src.Medium ?? p.Src.Large!,
                Title: BuildTitle(p, query),
                Photographer: p.Photographer ?? "Pexels",
                PhotographerUrl: p.PhotographerUrl ?? "https://www.pexels.com",
                SourceUrl: p.Url ?? "https://www.pexels.com",
                Width: p.Width,
                Height: p.Height,
                Query: query))
            .ToList();
    }

    /// <summary>Pexels alt text is often empty; fall back to the query so tiles are never unlabelled.</summary>
    private static string BuildTitle(PexelsPhoto photo, string query)
    {
        var alt = photo.Alt?.Trim();
        if (string.IsNullOrWhiteSpace(alt)) return query;
        return alt.Length > 200 ? alt[..200] : alt;
    }

    private class PexelsSearchResponse
    {
        [JsonPropertyName("photos")]
        public List<PexelsPhoto>? Photos { get; set; }

        [JsonPropertyName("next_page")]
        public string? NextPage { get; set; }
    }

    private class PexelsPhoto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("photographer")]
        public string? Photographer { get; set; }

        [JsonPropertyName("photographer_url")]
        public string? PhotographerUrl { get; set; }

        [JsonPropertyName("alt")]
        public string? Alt { get; set; }

        [JsonPropertyName("src")]
        public PexelsPhotoSrc? Src { get; set; }
    }

    private class PexelsPhotoSrc
    {
        [JsonPropertyName("large2x")]
        public string? Large2x { get; set; }

        [JsonPropertyName("large")]
        public string? Large { get; set; }

        [JsonPropertyName("medium")]
        public string? Medium { get; set; }
    }
}
