using Looxdex.Api.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Looxdex.Api.Services.Detection;

/// <summary>
/// Fetches ONNX weights on first use and caches them on disk.
///
/// The files are tens of megabytes each, so they are downloaded at runtime rather
/// than committed to the repository or baked into the container image. One instance
/// serves every model the app uses — the detector and the garment segmenter — keyed
/// by file name.
/// </summary>
public class ModelProvider
{
    public const string HttpClientName = "model-download";

    private readonly IHttpClientFactory _httpFactory;
    private readonly OnnxDetectionOptions _options;
    private readonly ILogger<ModelProvider> _logger;

    /// <summary>One gate per file, so two models can download concurrently.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<string, string> _cachedPaths = new();

    public ModelProvider(
        IHttpClientFactory httpFactory,
        IOptions<OnnxDetectionOptions> options,
        ILogger<ModelProvider> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Local path to the model, downloading it first if we do not have it yet.</summary>
    public async Task<string?> GetModelPathAsync(string url, string fileName, CancellationToken ct)
    {
        if (_cachedPaths.TryGetValue(fileName, out var known)) return known;

        var gate = _gates.GetOrAdd(fileName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cachedPaths.TryGetValue(fileName, out known)) return known;

            var directory = Path.GetFullPath(_options.CacheDirectory);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName);

            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            {
                _cachedPaths[fileName] = path;
                return path;
            }

            _logger.LogInformation("Downloading model {File} from {Url}...", fileName, url);

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
                            "Model download failed for {File} with {Status}.", fileName, response.StatusCode);
                        return null;
                    }

                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var destination = File.Create(tempPath);
                    await source.CopyToAsync(destination, ct);
                }

                File.Move(tempPath, path, overwrite: true);

                _logger.LogInformation(
                    "Model {File} ready ({Size:N0} bytes).", fileName, new FileInfo(path).Length);

                _cachedPaths[fileName] = path;
                return path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not download the model {File}.", fileName);
                TryDelete(tempPath);
                return null;
            }
        }
        finally
        {
            gate.Release();
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
