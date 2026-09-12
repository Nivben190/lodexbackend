namespace Looxdex.Api.Services.Detection;

/// <summary>
/// Downloads a feed image once per pass, so the detector and the cutout service
/// work from the same bytes instead of fetching the photo twice.
/// </summary>
public class ImageFetcher
{
    public const string HttpClientName = "detector-images";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ImageFetcher> _logger;

    public ImageFetcher(IHttpClientFactory httpFactory, ILogger<ImageFetcher> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<byte[]?> FetchAsync(string imageUrl, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(HttpClientName);
            using var response = await http.GetAsync(imageUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Image download failed for {Url}: {Status}.", imageUrl, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Image download errored for {Url}.", imageUrl);
            return null;
        }
    }
}
