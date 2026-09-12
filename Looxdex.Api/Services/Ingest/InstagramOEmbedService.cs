using System.Text.Json;
using System.Text.RegularExpressions;

namespace Looxdex.Api.Services.Ingest;

/// <param name="Html">Instagram's own embed markup, to be rendered as returned.</param>
/// <param name="Permalink">Canonical post URL, without whatever tracking the wearer pasted.</param>
/// <param name="Shortcode">The post id in the URL, used to keep the same look out of the feed twice.</param>
public record InstagramEmbed(string Html, string Permalink, string Shortcode);

/// <summary>
/// Resolves an Instagram post URL into the official embed for it.
///
/// Credit and display go through this rather than through anything we render
/// ourselves: the post is served by Instagram, stays attached to the creator, and
/// disappears from the feed by itself if she takes it down. What it deliberately
/// does *not* do is give us the photo — the endpoint returns markup and nothing
/// else, so the image a look is analysed from has to come from the wearer.
/// </summary>
public class InstagramOEmbedService
{
    /// <summary>Works unauthenticated for public posts; a token only adds fields we do not use.</summary>
    private const string Endpoint = "https://graph.facebook.com/v18.0/instagram_oembed";

    private static readonly Regex ShortcodePattern = new(
        @"instagram\.com/(?:p|reel|tv)/([A-Za-z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<InstagramOEmbedService> _logger;

    public InstagramOEmbedService(HttpClient http, ILogger<InstagramOEmbedService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>The post id in an Instagram URL, or null if it is not one.</summary>
    public static string? TryGetShortcode(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var match = ShortcodePattern.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Fetches the embed. Returns null when the post is private, deleted or simply
    /// not a post — the same answer Instagram gives, which is also the answer that
    /// should stop a look being filed under someone who never published it.
    /// </summary>
    public async Task<InstagramEmbed?> ResolveAsync(string url, CancellationToken ct)
    {
        var shortcode = TryGetShortcode(url);
        if (shortcode is null) return null;

        // Rebuilt from the shortcode: pasted links arrive carrying share tokens and
        // campaign parameters that have no business being stored.
        var permalink = $"https://www.instagram.com/p/{shortcode}/";

        try
        {
            using var response = await _http.GetAsync(
                $"{Endpoint}?url={Uri.EscapeDataString(permalink)}&omitscript=true", ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Instagram declined to embed {Shortcode}: {Status} {Body}",
                    shortcode, response.StatusCode, Truncate(body));
                return null;
            }

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("html", out var html))
            {
                _logger.LogInformation("Instagram returned no embed for {Shortcode}.", shortcode);
                return null;
            }

            return new InstagramEmbed(html.GetString() ?? string.Empty, permalink, shortcode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not resolve the Instagram embed for {Url}.", permalink);
            return null;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..200];
}
