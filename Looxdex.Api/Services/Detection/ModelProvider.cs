using Looxdex.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Looxdex.Api.Services.Detection;

/// <summary>
/// Fetches the ONNX weights on first use and caches them on disk.
///
/// The file is ~54MB, so it is downloaded at runtime rather than committed to
/// the repository or baked into the container image.
/// </summary>
public class ModelProvider
{
    public const string HttpClientName = "model-download";

    private readonly IHttpClientFactory _httpFactory;
    private readonly OnnxDetectionOptions _options;
    private readonly ILogger<ModelProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedPath;

    public ModelProvider(
        IHttpClientFactory httpFactory,
        IOptions<OnnxDetectionOptions> options,
        ILogger<ModelProvider> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetModelPathAsync(CancellationToken ct)
    {
        if (_cachedPath is not null) return _cachedPath;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cachedPath is not null) return _cachedPath;

            var directory = Path.GetFullPath(_options.CacheDirectory);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, _options.FileName);

            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            {
                _cachedPath = path;
                return path;
            }

            var url = _options.ModelUrl;
            _logger.LogInformation("Downloading detection model from {Url}...", url);

            // Download to a temp file first so an interrupted run cannot leave a
            // truncated model that looks valid on the next boot.
            var tempPath = path + ".partial";

            try
            {
                var http = _httpFactory.CreateClient(HttpClientName);
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError(
                            "Model download failed with {Status}.", response.StatusCode);
                        return null;
                    }

                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var destination = File.Create(tempPath);
                    await source.CopyToAsync(destination, ct);
                }

                File.Move(tempPath, path, overwrite: true);

                _logger.LogInformation(
                    "Detection model ready ({Size:N0} bytes).", new FileInfo(path).Length);

                _cachedPath = path;
                return path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not download the detection model.");
                TryDelete(tempPath);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }
}
