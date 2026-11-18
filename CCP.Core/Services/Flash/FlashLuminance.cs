using System;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>Rec.601 average of a decoded, premultiplied four-channel sample (WPF FlashService.cs:2288).</summary>
public static class FlashLuminance
{
    public static double Average(ReadOnlySpan<byte> pixels, bool bgra)
    {
        double sum = 0, weight = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            double alpha = pixels[i + 3] / 255.0;
            if (alpha == 0) continue;
            // The decoded pixels are premultiplied. Their channels already include alpha.
            double r = pixels[i + (bgra ? 2 : 0)] / 255.0;
            double g = pixels[i + 1] / 255.0;
            double b = pixels[i + (bgra ? 0 : 2)] / 255.0;
            sum += 0.299 * r + 0.587 * g + 0.114 * b;
            weight += alpha;
        }
        return weight == 0 ? 0 : Math.Clamp(sum / weight, 0, 1);
    }
}
