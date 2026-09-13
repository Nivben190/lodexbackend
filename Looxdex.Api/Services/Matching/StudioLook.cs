using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Looxdex.Api.Services.Matching;

/// <summary>
/// Tells a photograph taken to sell a garment from a photograph of someone out
/// wearing one.
///
/// Reverse image search cannot make that distinction, and its best likeness is
/// usually the wrong one of the two: our picture came out of a street photograph,
/// so another street photograph is the closer match. The tile is meant to show
/// the garment, so it prefers the shop's picture and lets likeness break ties.
///
/// What it measures is the background, not the person. An earlier version scored
/// skin and rejected every e-commerce photograph with a model in it — which is
/// most of them, and they are exactly the pictures a shop leads with. A studio
/// backdrop is plain, unsaturated and the same in all four corners; a street has
/// a wall at the top, a pavement at the bottom and something going on.
///
/// The one case it cannot call is a mirror photograph taken in a white room. That
/// is a plain even background by every measure available here, and at thumbnail
/// size there is nothing left to separate it from a studio.
/// </summary>
public static class StudioLook
{
    /// <summary>How much a picture looks like it was taken for a catalogue, 0 to 1.</summary>
    public static double Score(byte[] image)
    {
        try
        {
            using var photo = Image.Load<Rgb24>(image);

            if (photo.Width < 24 || photo.Height < 24) return 0;

            return Blandness(photo) * Evenness(photo);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>How plain and unsaturated the border is: paper rather than a scene.</summary>
    private static double Blandness(Image<Rgb24> photo)
    {
        var marginX = Math.Max(1, photo.Width / 10);
        var marginY = Math.Max(1, photo.Height / 10);

        double sum = 0, sumSquares = 0, saturation = 0;
        var count = 0;

        for (var y = 0; y < photo.Height; y += 2)
        {
            for (var x = 0; x < photo.Width; x += 2)
            {
                var onBorder = x < marginX || y < marginY
                               || x >= photo.Width - marginX
                               || y >= photo.Height - marginY;

                if (!onBorder) continue;

                var pixel = photo[x, y];
                var luma = 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;

                var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));

                sum += luma;
                sumSquares += luma * luma;
                saturation += max == 0 ? 0 : (max - min) / (double)max;
                count++;
            }
        }

        if (count == 0) return 0;

        var mean = sum / count;
        var variance = Math.Max(0, sumSquares / count - mean * mean);
        var deviation = Math.Sqrt(variance);

        var flat = Math.Max(0, 1 - Math.Min(deviation, 70) / 70);
        var grey = Math.Max(0, 1 - saturation / count * 2.2);

        return flat * grey;
    }

    /// <summary>
    /// Whether all four corners agree. A seamless backdrop is one surface; a room
    /// is a wall meeting a floor, and the corners say so.
    /// </summary>
    private static double Evenness(Image<Rgb24> photo)
    {
        var width = Math.Max(2, photo.Width / 7);
        var height = Math.Max(2, photo.Height / 7);

        Span<double> corners = stackalloc double[4];
        corners[0] = MeanLuma(photo, 0, 0, width, height);
        corners[1] = MeanLuma(photo, photo.Width - width, 0, photo.Width, height);
        corners[2] = MeanLuma(photo, 0, photo.Height - height, width, photo.Height);
        corners[3] = MeanLuma(photo, photo.Width - width, photo.Height - height, photo.Width, photo.Height);

        double low = corners[0], high = corners[0];

        foreach (var corner in corners)
        {
            low = Math.Min(low, corner);
            high = Math.Max(high, corner);
        }

        return Math.Max(0, 1 - (high - low) / 60);
    }

    private static double MeanLuma(Image<Rgb24> photo, int x0, int y0, int x1, int y1)
    {
        double sum = 0;
        var count = 0;

        for (var y = y0; y < y1; y += 2)
        {
            for (var x = x0; x < x1; x += 2)
            {
                var pixel = photo[x, y];
                sum += 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }
}
