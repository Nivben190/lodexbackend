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

/// <summary>What the segmenter has to say about a detection.</summary>
public enum MaskVerdict
{
    /// <summary>No class for this garment, so the segmenter has no opinion either way.</summary>
    NotSegmentable,

    /// <summary>Pixels of the right garment sit under the box: a second model agrees.</summary>
    Confirmed,

    /// <summary>Nothing of the kind is there — bare skin read as a top, a floor as a shoe.</summary>
    Absent
}

/// <param name="Verdict">Whether the segmenter found the garment the detector claims.</param>
/// <param name="Cutout">The tile, when the mask was solid enough to make one worth showing.</param>
public record GarmentRender(MaskVerdict Verdict, GarmentCutout? Cutout);

/// <param name="PersonPresent">
/// Whether anyone is wearing anything in this photo. The segmenter parses people,
/// so on a photo of a handbag on a bed its output is noise that can agree with
/// anything — and the detector, trained on worn clothing, is guessing too.
/// </param>
/// <param name="Items">What was found for each detection, by index.</param>
public record CutoutPass(bool PersonPresent, IReadOnlyDictionary<int, GarmentRender> Items);

public interface IGarmentCutoutService
{
    bool Enabled { get; }

    /// <summary>
    /// Segments the photo and reports on each detection: whether the garment is
    /// really there, and its cutout when one can be made. Keyed by the index of
    /// the hit in <paramref name="hits"/>.
    /// </summary>
    Task<CutoutPass> RenderAsync(
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

    public async Task<CutoutPass> RenderAsync(
        byte[] photo, IReadOnlyList<DetectionHit> hits, CancellationToken ct)
    {
        // Nothing known either way: assume a person, so a failure here cannot
        // quietly throw away everything the detector found.
        var empty = new CutoutPass(true, new Dictionary<int, GarmentRender>());

        if (!Enabled || hits.Count == 0) return empty;

        var session = await GetSessionAsync(ct);
        if (session is null) return empty;

        await _inferenceGate.WaitAsync(ct);
        try
        {
            using var image = Image.Load<Rgba32>(photo);
            var labels = Segment(session, image);
            var personPresent = HasPerson(labels);

            if (!personPresent)
            {
                _logger.LogInformation("No one is wearing anything in this photo.");
            }

            var results = new Dictionary<int, GarmentRender>();

            for (var i = 0; i < hits.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (!AtrClasses.TryGetValue(hits[i].Label, out var classes))
                {
                    results[i] = new GarmentRender(MaskVerdict.NotSegmentable, null);
                    continue;
                }

                results[i] = Render(image, hits[i], classes, labels);
            }

            return new CutoutPass(personPresent, results);
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

    /// <summary>
    /// Whether the photo has a person in it, by looking for the parts of one:
    /// a face, hair, an arm, a leg. Their absence means this is a flat-lay or a
    /// product shot, and everything the parser says about it should be distrusted.
    /// </summary>
    private static bool HasPerson(byte[,] labels)
    {
        // Hair, face, both legs, both arms.
        ReadOnlySpan<byte> body = stackalloc byte[] { 2, 11, 12, 13, 14, 15 };

        var height = labels.GetLength(0);
        var width = labels.GetLength(1);
        var found = 0;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (body.Contains(labels[y, x])) found++;
        }

        // A hand at the edge of the frame is still a person; a stray cell is not.
        return found > width * height * 0.005;
    }

    /// <summary>Masks one garment out of the photo and lays it on a square tile.</summary>
    private GarmentRender Render(Image<Rgba32> image, DetectionHit hit, int[] classes, byte[,] labels)
    {
        var mapHeight = labels.GetLength(0);
        var mapWidth = labels.GetLength(1);

        // Membership at map resolution, sampled bilinearly below. Taking the argmax
        // per pixel instead would staircase every edge into ten-pixel blocks.
        var membership = new float[mapHeight, mapWidth];
        for (var y = 0; y < mapHeight; y++)
        for (var x = 0; x < mapWidth; x++)
            membership[y, x] = Array.IndexOf(classes, labels[y, x]) >= 0 ? 1f : 0f;

        // The box says which garment; the mask says how far it goes. Keeping only
        // the blobs the box actually lands on is what separates this woman's coat
        // from the one standing beside her, and cropping to those blobs rather
        // than to the box is what stops a trouser leg being cut off at the knee.
        var box = new Rectangle(
            (int)Math.Round(hit.X / 100 * mapWidth),
            (int)Math.Round(hit.Y / 100 * mapHeight),
            Math.Max(1, (int)Math.Round(hit.Width / 100 * mapWidth)),
            Math.Max(1, (int)Math.Round(hit.Height / 100 * mapHeight)));

        var extent = KeepBlobsUnder(membership, mapHeight, mapWidth, box);

        // Nothing of this garment under the box. Said plainly rather than silently,
        // because it is the strongest evidence we have that the detection is wrong.
        if (extent is null) return new GarmentRender(MaskVerdict.Absent, null);

        // A margin of one cell: the blob edge is the mask's idea of the hem, and
        // the soft ramp below needs somewhere to fall off.
        var region = Rectangle.Intersect(
            new Rectangle(
                (int)((extent.Value.X - 1) / (double)mapWidth * image.Width),
                (int)((extent.Value.Y - 1) / (double)mapHeight * image.Height),
                (int)((extent.Value.Width + 2) / (double)mapWidth * image.Width),
                (int)((extent.Value.Height + 2) / (double)mapHeight * image.Height)),
            image.Bounds);

        if (region.Width < 8 || region.Height < 8)
        {
            return new GarmentRender(MaskVerdict.Confirmed, null);
        }

        using var crop = image.Clone(c => c.Crop(region));

        // The coarse stencil first: one value per pixel, read from the mask grid.
        var width = crop.Width;
        var height = crop.Height;
        var alpha = new float[width * height];
        var guide = new float[width * height];

        for (var y = 0; y < height; y++)
        {
            var v = (y + region.Y + 0.5) / image.Height * mapHeight - 0.5;

            for (var x = 0; x < width; x++)
            {
                var u = (x + region.X + 0.5) / image.Width * mapWidth - 0.5;
                var m = Sample(membership, u, v, mapWidth, mapHeight);

                var i = y * width + x;
                alpha[i] = (float)Math.Clamp((m - 0.45) / 0.15, 0, 1);

                var pixel = crop[x, y];
                guide[i] = (0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B) / 255f;
            }
        }

        // Then pull that stencil onto the garment's real edge. The mask knows where
        // the trousers are to within about a finger's width; the photograph knows
        // exactly where they stop. Filtering the mask against the image moves the
        // boundary to where the picture actually changes, which is the difference
        // between a cut-out and something torn out by hand.
        GuidedRefine(alpha, guide, width, height, _options.EdgeRefineRadius);

        int minX = width, minY = height, maxX = -1, maxY = -1;

        // Colour is accumulated per luminance bin so the naming pass can work from
        // the lit face of the garment. A flat average over every opaque pixel drags
        // a white shirt into grey, because half of it is in its own shadow.
        var lumaR = new long[256];
        var lumaG = new long[256];
        var lumaB = new long[256];
        var lumaCount = new int[256];
        var opaque = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Steepened around the halfway mark: the filter leaves a soft ramp
                // everywhere, and a garment edge is a line, not a gradient.
                var refined = Math.Clamp((alpha[y * width + x] - 0.42) / 0.22, 0, 1);

                var pixel = crop[x, y];
                pixel.A = (byte)(refined * 255);
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

            // The garment is there, it is just too thin to make a tile of — a belt,
            // a strap. Confirmed, so the detection stands; no cutout, so it keeps
            // the plain crop.
            return new GarmentRender(MaskVerdict.Confirmed, null);
        }

        using var trimmed = crop.Clone(c => c.Crop(
            new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));

        // A garment that occupies a couple of hundred pixels of the photo cannot be
        // made into a tile; enlarging it only makes a bigger blur. Better to hand
        // back the crop, which at least reads as a photograph of something.
        if (Math.Min(trimmed.Width, trimmed.Height) < _options.MinGarmentPixels)
        {
            _logger.LogInformation(
                "{Label} is only {Width}x{Height} in the photo; keeping the crop.",
                hit.Label, trimmed.Width, trimmed.Height);

            return new GarmentRender(MaskVerdict.Confirmed, null);
        }

        // How much of its own bounding box the garment actually fills. A dress fills
        // most of it; two scraps of trouser leg either side of a coat fill very
        // little, and that is exactly the tile nobody can read. The crop of the same
        // region at least shows the garment on the person, in context.
        var fill = opaque / (double)(trimmed.Width * trimmed.Height);

        if (fill < _options.MinShapeFill)
        {
            _logger.LogInformation(
                "{Label} fills only {Fill:P0} of its outline; too broken to read, keeping the crop.",
                hit.Label, fill);

            return new GarmentRender(MaskVerdict.Confirmed, null);
        }

        var tileSize = _options.TileSize;
        var inset = (int)(tileSize * _options.TileInset);
        var fitted = tileSize - inset * 2;

        // Never past 1:1. Beyond that the tile is inventing detail, and a garment
        // shown slightly small on its tile looks deliberate where a soft one does
        // not.
        var scale = Math.Min(
            Math.Min(fitted / (double)trimmed.Width, fitted / (double)trimmed.Height),
            1.0);
        var drawWidth = Math.Max(1, (int)(trimmed.Width * scale));
        var drawHeight = Math.Max(1, (int)(trimmed.Height * scale));

        // Lanczos on the way down: a garment shrunk with a soft filter loses the
        // weave, and the weave is most of what makes it read as cloth.
        using var scaled = trimmed.Clone(c => c.Resize(
            drawWidth, drawHeight, KnownResamplers.Lanczos3));

        // Transparent rather than a painted background: the tile colour belongs to
        // the client, which has both a light and a dark theme to satisfy.
        using var tile = new Image<Rgba32>(tileSize, tileSize, Color.Transparent);
        tile.Mutate(c => c.DrawImage(
            scaled, new Point((tileSize - drawWidth) / 2, (tileSize - drawHeight) / 2), 1f));

        using var buffer = new MemoryStream();
        tile.Save(buffer, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossy,
            Quality = 84
        });

        var dominant = DominantColor(lumaR, lumaG, lumaB, lumaCount, opaque);

        return new GarmentRender(
            MaskVerdict.Confirmed,
            new GarmentCutout(
                buffer.ToArray(),
                "image/webp",
                ColorNamer.Describe(dominant.R, dominant.G, dominant.B)));
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
    /// Keeps only the parts of the mask that belong to the garment the detector
    /// pointed at, and reports how far they reach.
    ///
    /// Blobs are whole connected regions of the class, found across the entire
    /// image rather than inside the box, so a coat that runs past the box is kept
    /// whole. A blob survives when the box covers a real share of it, or when it
    /// fills a real share of the box: the first keeps a long garment the box only
    /// caught the top of, the second keeps a jacket in two halves either side of
    /// an arm. Everything else — the next person along, a speck of wall the same
    /// colour — is erased.
    ///
    /// Returns the bounds of what survived, in map cells, or null if nothing did.
    /// </summary>
    private static Rectangle? KeepBlobsUnder(
        float[,] membership, int height, int width, Rectangle box)
    {
        var blobIds = new int[height, width];
        var sizes = new List<int> { 0 };      // index 0 is "unassigned"
        var inBox = new List<int> { 0 };
        var stack = new Stack<(int Y, int X)>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (membership[y, x] < 0.5f || blobIds[y, x] != 0) continue;

                var id = sizes.Count;
                var size = 0;
                var covered = 0;

                stack.Push((y, x));
                blobIds[y, x] = id;

                while (stack.Count > 0)
                {
                    var (cy, cx) = stack.Pop();
                    size++;
                    if (box.Contains(cx, cy)) covered++;

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
                inBox.Add(covered);
            }
        }

        var boxArea = Math.Max(1, box.Width * box.Height);
        var keep = new bool[sizes.Count];
        var kept = false;

        for (var id = 1; id < sizes.Count; id++)
        {
            var shareOfBlob = inBox[id] / (double)sizes[id];
            var shareOfBox = inBox[id] / (double)boxArea;

            if (shareOfBlob < 0.25 && shareOfBox < 0.12) continue;

            keep[id] = true;
            kept = true;
        }

        if (!kept) return null;

        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var id = blobIds[y, x];
            if (id == 0) continue;

            if (!keep[id])
            {
                membership[y, x] = 0f;
                continue;
            }

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// Snaps a coarse mask onto the edges of the image it came from.
    ///
    /// This is a guided filter: within a window, it fits the mask to the image as a
    /// straight line, alpha ≈ a·luminance + b, and keeps the fitted value. Where the
    /// photograph has an edge the fit follows it; where the photograph is flat the
    /// fit flattens, so noise inside the garment is smoothed while its outline
    /// sharpens. The whole thing is four box filters, each one a pair of running
    /// sums, so the cost does not depend on the radius.
    /// </summary>
    private static void GuidedRefine(float[] alpha, float[] guide, int width, int height, int radius)
    {
        const float epsilon = 1e-4f;   // tolerance for "this window is flat"

        var meanGuide = BoxBlur(guide, width, height, radius);
        var meanAlpha = BoxBlur(alpha, width, height, radius);

        var guideSquared = new float[guide.Length];
        var product = new float[guide.Length];

        for (var i = 0; i < guide.Length; i++)
        {
            guideSquared[i] = guide[i] * guide[i];
            product[i] = guide[i] * alpha[i];
        }

        var meanGuideSquared = BoxBlur(guideSquared, width, height, radius);
        var meanProduct = BoxBlur(product, width, height, radius);

        var a = new float[guide.Length];
        var b = new float[guide.Length];

        for (var i = 0; i < guide.Length; i++)
        {
            var variance = meanGuideSquared[i] - meanGuide[i] * meanGuide[i];
            var covariance = meanProduct[i] - meanGuide[i] * meanAlpha[i];

            a[i] = covariance / (variance + epsilon);
            b[i] = meanAlpha[i] - a[i] * meanGuide[i];
        }

        var meanA = BoxBlur(a, width, height, radius);
        var meanB = BoxBlur(b, width, height, radius);

        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = Math.Clamp(meanA[i] * guide[i] + meanB[i], 0f, 1f);
        }
    }

    /// <summary>Mean over a square window, as two separable passes over running sums.</summary>
    private static float[] BoxBlur(float[] source, int width, int height, int radius)
    {
        var horizontal = new float[source.Length];
        var result = new float[source.Length];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            float running = 0;

            for (var x = 0; x <= Math.Min(radius, width - 1); x++) running += source[row + x];

            for (var x = 0; x < width; x++)
            {
                var left = x - radius - 1;
                var right = x + radius;

                if (x > 0)
                {
                    if (right < width) running += source[row + right];
                    if (left >= 0) running -= source[row + left];
                }

                var from = Math.Max(0, x - radius);
                var to = Math.Min(width - 1, x + radius);
                horizontal[row + x] = running / (to - from + 1);
            }
        }

        for (var x = 0; x < width; x++)
        {
            float running = 0;

            for (var y = 0; y <= Math.Min(radius, height - 1); y++) running += horizontal[y * width + x];

            for (var y = 0; y < height; y++)
            {
                var above = y - radius - 1;
                var below = y + radius;

                if (y > 0)
                {
                    if (below < height) running += horizontal[below * width + x];
                    if (above >= 0) running -= horizontal[above * width + x];
                }

                var from = Math.Max(0, y - radius);
                var to = Math.Min(height - 1, y + radius);
                result[y * width + x] = running / (to - from + 1);
            }
        }

        return result;
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
