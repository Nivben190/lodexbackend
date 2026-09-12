namespace Looxdex.Api.Services.Detection;

public record NamedColor(string Name, string Hex);

/// <summary>
/// Names a garment's colour in the same vocabulary the closet filters use, so an
/// item detected in a photo is filterable next to one the wearer added by hand.
/// </summary>
public static class ColorNamer
{
    /// <summary>
    /// Mirrors CLOSET_COLORS in the web client; the two must stay in step. Each
    /// entry carries the four agreement forms Hebrew needs, so a colour can be
    /// attached to a garment noun without the adjective disagreeing with it.
    /// Colours borrowed from nouns — בז', בורדו, קרם, שמנת — do not inflect.
    /// </summary>
    private static readonly ColorEntry[] Palette =
    {
        new("לבן",   0xF7, 0xF5, 0xF0, "לבן", "לבנה", "לבנים", "לבנות"),
        new("שחור",  0x1A, 0x1A, 0x1A, "שחור", "שחורה", "שחורים", "שחורות"),
        new("בז'",   0xD8, 0xC9, 0xAE, "בז'", "בז'", "בז'", "בז'"),
        new("חום",   0x8B, 0x5E, 0x3C, "חום", "חומה", "חומים", "חומות"),
        new("כחול",  0x3B, 0x5A, 0x7A, "כחול", "כחולה", "כחולים", "כחולות"),
        new("בורדו", 0x81, 0x2D, 0x48, "בורדו", "בורדו", "בורדו", "בורדו"),
        new("שמנת",  0xF0, 0xE9, 0xDC, "שמנת", "שמנת", "שמנת", "שמנת"),
        new("אפור",  0x9A, 0x95, 0x8D, "אפור", "אפורה", "אפורים", "אפורות"),
        new("קרם",   0xEF, 0xE6, 0xD8, "קרם", "קרם", "קרם", "קרם")
    };

    private record ColorEntry(
        string Name,
        byte R,
        byte G,
        byte B,
        string MasculineSingular,
        string FeminineSingular,
        string MasculinePlural,
        string FemininePlural);

    /// <summary>
    /// Nearest palette entry, compared in a rough perceptual space rather than raw
    /// RGB: plain Euclidean distance calls navy "black" and cream "white" far too
    /// often, because it weighs lightness the same as hue.
    /// </summary>
    public static NamedColor Describe(byte r, byte g, byte b)
    {
        var best = Palette[0];
        var bestDistance = double.MaxValue;

        var (h1, s1, l1) = ToHsl(r, g, b);

        foreach (var candidate in Palette)
        {
            var (h2, s2, l2) = ToHsl(candidate.R, candidate.G, candidate.B);

            // Hue only means something once there is saturation to carry it.
            var hueGap = Math.Min(Math.Abs(h1 - h2), 360 - Math.Abs(h1 - h2)) / 180.0;
            var hueWeight = Math.Min(s1, s2) * 2.2;

            var distance = Math.Pow(l1 - l2, 2) * 1.6
                           + Math.Pow(s1 - s2, 2) * 0.9
                           + Math.Pow(hueGap, 2) * hueWeight;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return new NamedColor(best.Name, $"#{r:X2}{g:X2}{b:X2}");
    }

    /// <summary>The colour as an adjective agreeing with the garment it describes.</summary>
    public static string Inflect(string? colorName, HebrewForm form)
    {
        if (string.IsNullOrWhiteSpace(colorName)) return string.Empty;

        var entry = Palette.FirstOrDefault(c => c.Name == colorName.Trim());
        if (entry is null) return colorName;

        return form switch
        {
            HebrewForm.FeminineSingular => entry.FeminineSingular,
            HebrewForm.MasculinePlural => entry.MasculinePlural,
            HebrewForm.FemininePlural => entry.FemininePlural,
            _ => entry.MasculineSingular
        };
    }

    private static (double H, double S, double L) ToHsl(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;

        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var l = (max + min) / 2;
        var delta = max - min;

        if (delta < 1e-6) return (0, 0, l);

        var s = l > 0.5 ? delta / (2 - max - min) : delta / (max + min);

        // Saturation is unreliable at the ends of the range: eight levels of blue
        // in a near-black pixel reads as vividly saturated, which is how black
        // denim ends up filed under "blue". Fade it out where it stops meaning
        // anything.
        s *= Math.Clamp(l / 0.15, 0, 1) * Math.Clamp((1 - l) / 0.12, 0, 1);

        double h;
        if (max == rd) h = (gd - bd) / delta + (gd < bd ? 6 : 0);
        else if (max == gd) h = (bd - rd) / delta + 2;
        else h = (rd - gd) / delta + 4;

        return (h * 60, s, l);
    }
}
