using Windows.UI;

namespace Takupoke.Win;

public sealed partial class MainWindow
{
    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
    private static double ContrastRatio(Color first, Color second)
    {
        var firstLight = RelativeLuminance(first); var secondLight = RelativeLuminance(second);
        return (Math.Max(firstLight, secondLight) + 0.05) / (Math.Min(firstLight, secondLight) + 0.05);
    }
    private static Color ReadableTextColor(Color tint, bool dark)
    {
        // The strictest of the page/card surfaces used by this app. Moving text
        // toward black/white preserves the chosen hue and the original fill tint.
        var surface = dark ? Color.FromArgb(255, 43, 43, 43) : Color.FromArgb(255, 243, 243, 243);
        var target = dark ? (byte)255 : (byte)0;
        for (var step = 0; step <= 100; step++)
        {
            var amount = step / 100d;
            var candidate = Color.FromArgb(255,
                (byte)Math.Round(tint.R + (target - tint.R) * amount),
                (byte)Math.Round(tint.G + (target - tint.G) * amount),
                (byte)Math.Round(tint.B + (target - tint.B) * amount));
            if (ContrastRatio(candidate, surface) >= 4.5) return candidate;
        }
        return Color.FromArgb(255, target, target, target);
    }
    private static Color TextOnTint(Color tint)
    {
        var black = Color.FromArgb(255, 0, 0, 0); var white = Color.FromArgb(255, 255, 255, 255);
        return ContrastRatio(black, tint) >= ContrastRatio(white, tint) ? black : white;
    }
    private static double ReadableFillOpacity(Color tint, Color text, bool dark, double preferredOpacity)
    {
        // Hover/pressed fill opacity can reduce a formerly readable white label
        // on a light surface. Retain the tint and as much of the state opacity as
        // possible, increasing opacity only when that label would fall below 4.5.
        var surfaces = dark ? new[] { Color.FromArgb(255, 32, 32, 32), Color.FromArgb(255, 43, 43, 43) }
            : new[] { Color.FromArgb(255, 243, 243, 243), Color.FromArgb(255, 255, 255, 255) };
        for (var step = 0; step <= 100; step++)
        {
            var opacity = preferredOpacity + (1 - preferredOpacity) * step / 100;
            if (surfaces.All(surface => ContrastRatio(text, CompositeTint(tint, surface, opacity)) >= 4.5)) return opacity;
        }
        return 1;
    }
    private static Color CompositeTint(Color tint, Color surface, double opacity) => Color.FromArgb(255,
        (byte)Math.Round(tint.R * opacity + surface.R * (1 - opacity)),
        (byte)Math.Round(tint.G * opacity + surface.G * (1 - opacity)),
        (byte)Math.Round(tint.B * opacity + surface.B * (1 - opacity)));
}
