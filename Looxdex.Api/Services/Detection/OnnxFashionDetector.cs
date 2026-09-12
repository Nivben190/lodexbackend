using Looxdex.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Looxdex.Api.Services.Detection;

/// <summary>A wearable item found in an image, with the box already in percentages.</summary>
public record DetectionHit(
    string Label,
    string LabelHe,
    string Category,
    double Score,
    double X,
    double Y,
    double Width,
    double Height);

public interface IFashionDetector
{
    bool Enabled { get; }

    Task<IReadOnlyList<DetectionHit>> DetectAsync(string imageUrl, CancellationToken ct);

    /// <summary>
    /// Same pass over bytes already in hand. The cutout stage needs the very same
    /// photo, and fetching it twice doubles the egress for no gain.
    /// </summary>
    Task<IReadOnlyList<DetectionHit>> DetectAsync(byte[] photo, CancellationToken ct);
}

/// <summary>
/// Fashion object detection with YOLOS (fine-tuned on Fashionpedia) running
/// in-process through ONNX Runtime.
///
/// Inference happens on the background worker, once per image, and the result is
/// cached in the database — so CPU cost is bounded by how many images we ingest,
/// never by how much traffic the feed gets.
/// </summary>
public class OnnxFashionDetector : IFashionDetector, IDisposable
{
    // ImageNet statistics, matching the preprocessing the model was trained with.
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    private readonly ImageFetcher _fetcher;
    private readonly ModelProvider _modelProvider;
    private readonly OnnxDetectionOptions _options;
    private readonly ILogger<OnnxFashionDetector> _logger;

    private readonly SemaphoreSlim _sessionGate = new(1, 1);

    /// <summary>Serialises inference: one session, one image at a time, predictable memory.</summary>
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);

    private InferenceSession? _session;
    private string? _inputName;
    private string? _logitsName;
    private string? _boxesName;

    public OnnxFashionDetector(
        ImageFetcher fetcher,
        ModelProvider modelProvider,
        IOptions<OnnxDetectionOptions> options,
        ILogger<OnnxFashionDetector> logger)
    {
        _fetcher = fetcher;
        _modelProvider = modelProvider;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyList<DetectionHit>> DetectAsync(string imageUrl, CancellationToken ct)
    {
        if (!Enabled) return Array.Empty<DetectionHit>();

        var bytes = await _fetcher.FetchAsync(imageUrl, ct);
        if (bytes is null) return Array.Empty<DetectionHit>();

        return await DetectAsync(bytes, ct);
    }

    public async Task<IReadOnlyList<DetectionHit>> DetectAsync(byte[] photo, CancellationToken ct)
    {
        if (!Enabled) return Array.Empty<DetectionHit>();

        var session = await GetSessionAsync(ct);
        if (session is null) return Array.Empty<DetectionHit>();

        await _inferenceGate.WaitAsync(ct);
        try
        {
            using var image = Image.Load<Rgb24>(photo);

            var input = BuildInputTensor(image);

            using var results = session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(_inputName!, input)
            });

            var logits = results.First(r => r.Name == _logitsName).AsTensor<float>();
            var boxes = results.First(r => r.Name == _boxesName).AsTensor<float>();

            return PostProcess(logits, boxes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Detection pass failed.");
            return Array.Empty<DetectionHit>();
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    /// <summary>
    /// Resize preserving aspect ratio and normalise into an NCHW tensor.
    ///
    /// The model's input is fully dynamic, and YOLOS was trained the DETR way:
    /// scale the shortest edge to a target, cap the longest, keep the aspect.
    /// Stretching a portrait photo into a landscape frame distorts every body
    /// proportion and makes the detector hallucinate — hair reads as a hat, a
    /// chair back as a bag. Boxes come back normalised to this same frame, so
    /// keeping the aspect also keeps the mapping to percentages exact.
    /// </summary>
    private DenseTensor<float> BuildInputTensor(Image<Rgb24> image)
    {
        var (width, height) = TargetSize(image.Width, image.Height);

        using var resized = image.Clone(c => c.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = ResizeMode.Stretch,   // exact size; the size itself keeps the ratio
            Sampler = KnownResamplers.Bicubic
        }));

        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        resized.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    tensor[0, 0, y, x] = (pixel.R / 255f - Mean[0]) / Std[0];
                    tensor[0, 1, y, x] = (pixel.G / 255f - Mean[1]) / Std[1];
                    tensor[0, 2, y, x] = (pixel.B / 255f - Mean[2]) / Std[2];
                }
            }
        });

        return tensor;
    }

    /// <summary>
    /// DETR-style sizing: scale the shortest edge up to <c>ShortestEdge</c> without
    /// letting the longest exceed <c>LongestEdge</c>, then round to the patch grid.
    /// </summary>
    private (int Width, int Height) TargetSize(int sourceWidth, int sourceHeight)
    {
        double shortest = _options.ShortestEdge;
        double longest = _options.LongestEdge;

        var minSide = Math.Min(sourceWidth, sourceHeight);
        var maxSide = Math.Max(sourceWidth, sourceHeight);

        var scale = Math.Min(shortest / minSide, longest / maxSide);

        var width = RoundToPatch(sourceWidth * scale);
        var height = RoundToPatch(sourceHeight * scale);

        return (width, height);
    }

    /// <summary>YOLOS splits the image into 16px patches, so both sides must divide by 16.</summary>
    private static int RoundToPatch(double value)
    {
        const int patch = 16;
        var rounded = (int)Math.Round(value / patch) * patch;
        return Math.Max(patch * 2, rounded);
    }

    /// <summary>
    /// YOLOS emits a fixed set of queries. Each carries class logits (with a
    /// trailing "no object" class) and a box as centre-x, centre-y, width, height
    /// normalised to 0–1. Convert to top-left percentages for the overlay.
    /// </summary>
    private IReadOnlyList<DetectionHit> PostProcess(Tensor<float> logits, Tensor<float> boxes)
    {
        var queries = logits.Dimensions[1];
        var classCount = logits.Dimensions[2];
        var hits = new List<DetectionHit>();

        for (var q = 0; q < queries; q++)
        {
            // Softmax over the real classes only; the last column is "no object".
            var max = float.NegativeInfinity;
            for (var c = 0; c < classCount; c++)
            {
                var v = logits[0, q, c];
                if (v > max) max = v;
            }

            double sum = 0;
            for (var c = 0; c < classCount; c++) sum += Math.Exp(logits[0, q, c] - max);
            if (sum <= 0) continue;

            var bestClass = -1;
            double bestScore = 0;

            for (var c = 0; c < classCount - 1; c++)
            {
                var p = Math.Exp(logits[0, q, c] - max) / sum;
                if (p > bestScore)
                {
                    bestScore = p;
                    bestClass = c;
                }
            }

            if (bestClass < 0 || bestScore < _options.MinScore) continue;

            var rawLabel = FashionpediaLabels.LabelForIndex(bestClass);
            var mapped = FashionpediaLabels.Resolve(rawLabel);
            if (mapped is null) continue;   // a garment part, not an item

            var cx = boxes[0, q, 0];
            var cy = boxes[0, q, 1];
            var w = boxes[0, q, 2];
            var h = boxes[0, q, 3];

            var x = (cx - w / 2) * 100;
            var y = (cy - h / 2) * 100;
            var boxW = w * 100;
            var boxH = h * 100;

            // Clamp to the frame; a partially out-of-frame box is still useful.
            var left = Math.Clamp(x, 0, 100);
            var top = Math.Clamp(y, 0, 100);
            var right = Math.Clamp(x + boxW, 0, 100);
            var bottom = Math.Clamp(y + boxH, 0, 100);

            var finalW = right - left;
            var finalH = bottom - top;

            if (finalW <= 1 || finalH <= 1) continue;
            if (finalW >= 97 && finalH >= 97) continue;   // covers the whole image

            hits.Add(new DetectionHit(
                Label: rawLabel ?? string.Empty,
                LabelHe: mapped.LabelHe,
                Category: mapped.Category,
                Score: bestScore,
                X: Math.Round(left, 2),
                Y: Math.Round(top, 2),
                Width: Math.Round(finalW, 2),
                Height: Math.Round(finalH, 2)));
        }

        // One box per garment type, highest confidence wins: the model commonly
        // fires several overlapping queries on the same shirt.
        return hits
            .OrderByDescending(h => h.Score)
            .GroupBy(h => h.Label)
            .Select(g => g.First())
            .Take(_options.MaxItemsPerImage)
            .ToList();
    }

    private async Task<InferenceSession?> GetSessionAsync(CancellationToken ct)
    {
        if (_session is not null) return _session;

        await _sessionGate.WaitAsync(ct);
        try
        {
            if (_session is not null) return _session;

            var modelPath = await _modelProvider.GetModelPathAsync(
                _options.ModelUrl, _options.FileName, ct);
            if (modelPath is null) return null;

            // Fully qualified: ASP.NET Core also defines a SessionOptions.
            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                IntraOpNumThreads = _options.IntraOpThreads,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var session = new InferenceSession(modelPath, sessionOptions);

            // Read tensor names from the model rather than hardcoding them, so a
            // different export of the same architecture still loads.
            _inputName = session.InputMetadata.Keys.First();

            var outputs = session.OutputMetadata.Keys.ToList();
            _logitsName = outputs.FirstOrDefault(o => o.Contains("logit", StringComparison.OrdinalIgnoreCase))
                          ?? outputs[0];
            _boxesName = outputs.FirstOrDefault(o => o.Contains("box", StringComparison.OrdinalIgnoreCase))
                         ?? outputs.Last();

            _logger.LogInformation(
                "Detection model loaded. input={Input} logits={Logits} boxes={Boxes}",
                _inputName, _logitsName, _boxesName);

            _session = session;
            return _session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not initialise the ONNX detection session.");
            return null;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _sessionGate.Dispose();
        _inferenceGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
