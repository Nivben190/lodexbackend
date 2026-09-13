using Looxdex.Api.Configuration;
using Looxdex.Api.Services.Detection;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Looxdex.Api.Services.Matching;

public interface IClipEmbedder
{
    bool Enabled { get; }

    /// <summary>
    /// Embeds an image into the vector space shared by garments and products.
    /// Returns null when the model is unavailable or the bytes will not decode.
    /// </summary>
    Task<float[]?> EmbedAsync(byte[] image, CancellationToken ct);

    /// <summary>Names the model, so a stored vector can say what made it.</summary>
    string Signature { get; }
}

/// <summary>
/// Turns a picture of clothing into a vector, so a garment worn in a photograph
/// and the same garment on a shop's white background land near each other.
///
/// CLIP is the right tool for this precisely because it was never trained on
/// either: it learned what things look like from captions, so it encodes "wide
/// cream trousers" as wide cream trousers whether they are on a person, a hanger
/// or a studio floor. Both sides go through the same encoder, so the projection
/// head is unnecessary — only the direction of the vectors matters.
/// </summary>
public class ClipEmbedder : IClipEmbedder, IDisposable
{
    /// <summary>CLIP's own normalisation, which is not ImageNet's.</summary>
    private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
    private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

    private const int InputSize = 224;

    private readonly ModelProvider _modelProvider;
    private readonly ProductMatchOptions _options;
    private readonly ILogger<ClipEmbedder> _logger;

    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);

    private InferenceSession? _session;
    private string? _inputName;
    private string? _outputName;

    public ClipEmbedder(
        ModelProvider modelProvider,
        IOptions<ProductMatchOptions> options,
        ILogger<ClipEmbedder> logger)
    {
        _modelProvider = modelProvider;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public string Signature => _options.FileName;

    public async Task<float[]?> EmbedAsync(byte[] image, CancellationToken ct)
    {
        if (!Enabled) return null;

        var session = await GetSessionAsync(ct);
        if (session is null) return null;

        await _inferenceGate.WaitAsync(ct);
        try
        {
            using var source = Image.Load<Rgba32>(image);

            // Flattened onto white first. A cutout arrives on transparency, and
            // transparency reads as black once it becomes a tensor — which would
            // make every garment look like it was photographed at midnight, while
            // the shop's photograph of the same thing sits on white.
            using var flattened = new Image<Rgb24>(source.Width, source.Height, Color.White);
            flattened.Mutate(c => c.DrawImage(source, 1f));

            flattened.Mutate(c => c.Resize(new ResizeOptions
            {
                Size = new Size(InputSize, InputSize),
                Mode = ResizeMode.Pad,          // the whole garment, not a centre crop of it
                PadColor = Color.White,
                Sampler = KnownResamplers.Bicubic
            }));

            var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });

            flattened.ProcessPixelRows(accessor =>
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

            using var results = session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(_inputName!, tensor)
            });

            var output = results.First(r => r.Name == _outputName).AsTensor<float>();
            var length = output.Dimensions[^1];
            var vector = new float[length];

            for (var i = 0; i < length; i++) vector[i] = output.GetValue(i);

            Normalise(vector);
            return vector;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not embed an image.");
            return null;
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    /// <summary>
    /// Scales a vector to unit length, which turns the cosine of the angle between
    /// two of them into a plain dot product.
    /// </summary>
    public static void Normalise(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector) sum += value * value;

        var length = Math.Sqrt(sum);
        if (length < 1e-9) return;

        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / length);
    }

    /// <summary>Similarity of two unit vectors, from -1 to 1.</summary>
    public static double Similarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -1;

        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];

        return sum;
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

            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var session = new InferenceSession(modelPath, sessionOptions);

            _inputName = session.InputMetadata.Keys.First();

            // The pooled vector, whatever this export happens to call it.
            var outputs = session.OutputMetadata.Keys.ToList();
            _outputName = outputs.FirstOrDefault(o =>
                              o.Contains("pool", StringComparison.OrdinalIgnoreCase)
                              || o.Contains("embed", StringComparison.OrdinalIgnoreCase))
                          ?? outputs.Last();

            _logger.LogInformation(
                "CLIP loaded. input={Input} output={Output}", _inputName, _outputName);

            _session = session;
            return _session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not initialise CLIP.");
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
