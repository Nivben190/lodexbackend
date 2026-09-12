using Looxdex.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Looxdex.Api.Services.Detection;

/// <summary>A garment lifted out of a photo, ready to show as a product tile.</summary>
public record GarmentCutout(byte[] Image, string ContentType, NamedColor Color);

public interface IGarmentCutoutService
{
    bool Enabled { get; }

    /// <summary>
    /// Cuts each detection out of the photo. Keyed by the index of the hit in
    /// <paramref name="hits"/>; an item with no usable mask is simply absent.
    /// </summary>
    Task<IReadOnlyDictionary<int, GarmentCutout>> RenderAsync(
        byte[] photo, IReadOnlyList<DetectionHit> hits, CancellationToken ct);
}

/// <summary>
/// Turns a detection box into a product shot.
///
/// The detector gives a rectangle, and a rectangle of a street photo still holds a
/// face, two hands and half a room. SegFormer (fine-tuned on ATR) labels every
/// pixel as upper-clothes, pants, belt, bag and so on, so intersecting its mask
/// with the box leaves the garment alone on a transparent background.
///
/// One segmentation pass covers every item in an image, which is what keeps this
/// affordable: the cost is per photo, not per detection.
/// </summary>
public class GarmentCutoutService : IGarmentCutoutService, IDisposable
{
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    /// <summary>
    /// Fashionpedia label to the ATR classes that make up that garment. Labels the
    /// segmenter has no class for — glasses, watch, tie — are absent, and those
    /// items keep the plain crop.
    /// </summary>
    private static readonly Dictionary<string, int[]> AtrClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shirt, blouse"] = new[] { 4 },
        ["top, t-shirt, sweatshirt"] = new[] { 4 },
        ["sweater"] = new[] { 4 },
        ["cardigan"] = new[] { 4 },
        ["jacket"] = new[] { 4 },
        ["vest"] = new[] { 4 },
        ["coat"] = new[] { 4 },
        ["cape"] = new[] { 4 },
        ["pants"] = new[] { 6 },
        ["shorts"] = new[] { 6 },
        ["skirt"] = new[] { 5 },
        ["dress"] = new[] { 7 },
        ["jumpsuit"] = new[] { 7 },
        ["belt"] = new[] { 8 },
        ["shoe"] = new[] { 9, 10 },
        ["bag, wallet"] = new[] { 16 },
        ["scarf"] = new[] { 17 },
        ["hat"] = new[] { 1 }
    };

    private readonly ModelProvider _modelProvider;
    private readonly GarmentCutoutOptions _options;
    private readonly ILogger<GarmentCutoutService> _logger;

    private readonly SemaphoreSlim _sessionGate = new(1, 1);

    /// <summary>One session, one image at a time — the same bargain the detector makes.</summary>
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);

    private InferenceSession? _session;
    private string? _inputName;
    private string? _outputName;

    public GarmentCutoutService(
        ModelProvider modelProvider,
        IOptions<GarmentCutoutOptions> options,
        ILogger<GarmentCutoutService> logger)
    {
        _modelProvider = modelProvider;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyDictionary<int, GarmentCutout>> RenderAsync(
        byte[] photo, IReadOnlyList<DetectionHit> hits, CancellationToken ct)
    {
        var empty = (IReadOnlyDictionary<int, GarmentCutout>)new Dictionary<int, GarmentCutout>();

        if (!Enabled || hits.Count == 0) return empty;

        var session = await GetSessionAsync(ct);
        if (session is null) return empty;

        await _inferenceGate.WaitAsync(ct);
        try
        {
            using var image = Image.Load<Rgba32>(photo);
            var labels = Segment(session, image);

            var results = new Dictionary<int, GarmentCutout>();

            for (var i = 0; i < hits.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (!AtrClasses.TryGetValue(hits[i].Label, out var classes)) continue;

                var cutout = Render(image, hits[i], classes, labels);
                if (cutout is not null) results[i] = cutout;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cutout pass failed; items keep their plain crop.");
            return empty;
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    /// <summary>Runs the segmenter and reduces its logits to one class id per cell.</summary>
    private byte[,] Segment(InferenceSession session, Image<Rgba32> image)
    {
        var size = _options.InputSize;

        using var resized = image.Clone(c => c.Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Mode = ResizeMode.Stretch,   // the exported processor squares the input
            Sampler = KnownResamplers.Bicubic
        }));

        var input = new DenseTensor<float>(new[] { 1, 3, size, size });

        resized.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    input[0, 0, y, x] = (pixel.R / 255f - Mean[0]) / Std[0];
                    input[0, 1, y, x] = (pixel.G / 255f - Mean[1]) / Std[1];
                    input[0, 2, y, x] = (pixel.B / 255f - Mean[2]) / Std[2];
                }
            }
        });

        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(_inputName!, input)
        });

        var logits = results.First(r => r.Name == _outputName).AsTensor<float>();
        var classCount = logits.Dimensions[1];
        var height = logits.Dimensions[2];
        var width = logits.Dimensions[3];

        var map = new byte[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var best = 0;
                var bestScore = float.NegativeInfinity;

                for (var c = 0; c < classCount; c++)
                {
                    var score = logits[0, c, y, x];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }

                map[y, x] = (byte)best;
            }
        }

        return map;
    }

    /// <summary>Masks one garment out of the photo and lays it on a square tile.</summary>
    private GarmentCutout? Render(Image<Rgba32> image, DetectionHit hit, int[] classes, byte[,] labels)
    {
        var mapHeight = labels.GetLength(0);
        var mapWidth = labels.GetLength(1);

        // Grow the box before masking: YOLOS clips sleeves and hems, and out there
        // the mask — not the rectangle — is what decides where the garment ends.
        var pad = _options.BoxPadding;
        var boxX = hit.X / 100 * image.Width;
        var boxY = hit.Y / 100 * image.Height;
        var boxW = hit.Width / 100 * image.Width;
        var boxH = hit.Height / 100 * image.Height;

        var region = Rectangle.Intersect(
            new Rectangle(
                (int)(boxX - boxW * pad),
                (int)(boxY - boxH * pad),
                (int)(boxW * (1 + pad * 2)),
                (int)(boxH * (1 + pad * 2))),
            image.Bounds);

        if (region.Width < 8 || region.Height < 8) return null;

        using var crop = image.Clone(c => c.Crop(region));

        // Membership at map resolution, sampled bilinearly below. Taking the argmax
        // per pixel instead would staircase every edge into ten-pixel blocks.
        var membership = new float[mapHeight, mapWidth];
        for (var y = 0; y < mapHeight; y++)
        for (var x = 0; x < mapWidth; x++)
            membership[y, x] = Array.IndexOf(classes, labels[y, x]) >= 0 ? 1f : 0f;

        KeepLargestBlobs(membership, mapHeight, mapWidth);

        int minX = crop.Width, minY = crop.Height, maxX = -1, maxY = -1;

        // Colour is accumulated per luminance bin so the naming pass can work from
        // the lit face of the garment. A flat average over every opaque pixel drags
        // a white shirt into grey, because half of it is in its own shadow.
        var lumaR = new long[256];
        var lumaG = new long[256];
        var lumaB = new long[256];
        var lumaCount = new int[256];
        var opaque = 0;

        for (var y = 0; y < crop.Height; y++)
        {
            var v = (y + region.Y + 0.5) / image.Height * mapHeight - 0.5;

            for (var x = 0; x < crop.Width; x++)
            {
                var u = (x + region.X + 0.5) / image.Width * mapWidth - 0.5;
                var m = Sample(membership, u, v, mapWidth, mapHeight);

                // A narrow ramp: wide enough to soften the staircase, not so wide
                // that a halo of the room survives around the shoulders.
                var alpha = Math.Clamp((m - 0.45) / 0.15, 0, 1);
                var pixel = crop[x, y];
                pixel.A = (byte)(alpha * 255);
                crop[x, y] = pixel;

                if (pixel.A <= 32) continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;

                if (pixel.A < 200) continue;
                opaque++;

                var luma = (byte)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                lumaR[luma] += pixel.R;
                lumaG[luma] += pixel.G;
                lumaB[luma] += pixel.B;
                lumaCount[luma]++;
            }
        }

        if (maxX < 0 || opaque < _options.MinMaskPixels)
        {
            _logger.LogInformation(
                "No usable mask for {Label}: {Pixels} solid px against a floor of {Floor}; "
                + "keeping the crop.", hit.Label, opaque, _options.MinMaskPixels);
            return null;
        }

        using var trimmed = crop.Clone(c => c.Crop(
            new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));

        var tileSize = _options.TileSize;
        var inset = (int)(tileSize * _options.TileInset);
        var box = tileSize - inset * 2;

        var scale = Math.Min(box / (double)trimmed.Width, box / (double)trimmed.Height);
        var width = Math.Max(1, (int)(trimmed.Width * scale));
        var height = Math.Max(1, (int)(trimmed.Height * scale));

        using var scaled = trimmed.Clone(c => c.Resize(width, height, KnownResamplers.Bicubic));

        // Transparent rather than a painted background: the tile colour belongs to
        // the client, which has both a light and a dark theme to satisfy.
        using var tile = new Image<Rgba32>(tileSize, tileSize, Color.Transparent);
        tile.Mutate(c => c.DrawImage(scaled, new Point((tileSize - width) / 2, (tileSize - height) / 2), 1f));

        using var buffer = new MemoryStream();
        tile.Save(buffer, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossy,
            Quality = 72
        });

        var dominant = DominantColor(lumaR, lumaG, lumaB, lumaCount, opaque);

        return new GarmentCutout(
            buffer.ToArray(),
            "image/webp",
            ColorNamer.Describe(dominant.R, dominant.G, dominant.B));
    }

    /// <summary>
    /// The garment's colour, read from its lit face.
    ///
    /// Averages the band between the 55th and 90th luminance percentile: bright
    /// enough to escape the fold shadows that grey out every pale garment, dark
    /// enough to ignore specular highlights, which are the colour of the lamp
    /// rather than of the cloth.
    /// </summary>
    private static (byte R, byte G, byte B) DominantColor(
        long[] r, long[] g, long[] b, int[] counts, int total)
    {
        var lower = (int)(total * 0.55);
        var upper = (int)(total * 0.90);

        long sumR = 0, sumG = 0, sumB = 0, taken = 0, seen = 0;

        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0) continue;

            seen += counts[i];
            if (seen < lower) continue;

            sumR += r[i];
            sumG += g[i];
            sumB += b[i];
            taken += counts[i];

            if (seen >= upper) break;
        }

        if (taken == 0)
        {
            for (var i = 0; i < counts.Length; i++)
            {
                sumR += r[i];
                sumG += g[i];
                sumB += b[i];
                taken += counts[i];
            }
        }

        taken = Math.Max(taken, 1);
        return ((byte)(sumR / taken), (byte)(sumG / taken), (byte)(sumB / taken));
    }

    /// <summary>
    /// Drops stray islands from a membership map, keeping the biggest blob and any
    /// comparable sibling — a pair of shoes is two blobs, a speck of shirt-coloured
    /// wall behind the model is not.
    /// </summary>
    private static void KeepLargestBlobs(float[,] membership, int height, int width)
    {
        var blobIds = new int[height, width];
        var sizes = new List<int> { 0 };   // index 0 is "unassigned"
        var stack = new Stack<(int Y, int X)>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (membership[y, x] < 0.5f || blobIds[y, x] != 0) continue;

                var id = sizes.Count;
                var size = 0;
                stack.Push((y, x));
                blobIds[y, x] = id;

                while (stack.Count > 0)
                {
                    var (cy, cx) = stack.Pop();
                    size++;

                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var ny = cy + dy;
                        var nx = cx + dx;

                        if (ny < 0 || nx < 0 || ny >= height || nx >= width) continue;
                        if (blobIds[ny, nx] != 0 || membership[ny, nx] < 0.5f) continue;

                        blobIds[ny, nx] = id;
                        stack.Push((ny, nx));
                    }
                }

                sizes.Add(size);
            }
        }

        if (sizes.Count <= 2) return;   // nothing found, or a single blob already

        var largest = sizes.Skip(1).Max();
        var floor = largest * 0.2;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var id = blobIds[y, x];
            if (id != 0 && sizes[id] < floor) membership[y, x] = 0f;
        }
    }

    /// <summary>Bilinear read of the membership map at a fractional cell position.</summary>
    private static double Sample(float[,] map, double u, double v, int width, int height)
    {
        var x0 = (int)Math.Floor(u);
        var y0 = (int)Math.Floor(v);
        var fx = u - x0;
        var fy = v - y0;

        var x1 = Math.Clamp(x0 + 1, 0, width - 1);
        var y1 = Math.Clamp(y0 + 1, 0, height - 1);
        x0 = Math.Clamp(x0, 0, width - 1);
        y0 = Math.Clamp(y0, 0, height - 1);

        var top = map[y0, x0] * (1 - fx) + map[y0, x1] * fx;
        var bottom = map[y1, x0] * (1 - fx) + map[y1, x1] * fx;

        return top * (1 - fy) + bottom * fy;
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
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var session = new InferenceSession(modelPath, sessionOptions);

            _inputName = session.InputMetadata.Keys.First();
            _outputName = session.OutputMetadata.Keys.First();

            _logger.LogInformation(
                "Garment segmenter loaded. input={Input} output={Output}", _inputName, _outputName);

            _session = session;
            return _session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not initialise the garment segmentation session.");
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
