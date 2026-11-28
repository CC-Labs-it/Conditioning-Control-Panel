using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal static partial class FlashOverlay
{
    private static readonly Dictionary<string, double> LuminanceCache = new(StringComparer.OrdinalIgnoreCase);
    internal static Action<double, int> SetLuminance = (value, ms) => CoreHaptics.Service?.SetLayer(HapticLayer.Luminance, value, ms);

    // WPF FlashService.cs:2246-2310: sample the displayed frame once per file.
    internal static void ApplyLuminance(Bitmap bitmap, string path, TimeSpan lifetime)
    {
        try
        {
            var settings = CoreHaptics.Service?.Settings;
            if (settings == null || !settings.LuminanceSyncEnabled || settings.LuminanceSyncIntensity <= 0) return;
            if (string.IsNullOrEmpty(path) || !LuminanceCache.TryGetValue(path, out var cached))
            {
                using var sample = bitmap.CreateScaledBitmap(new PixelSize(Math.Min(8, bitmap.PixelSize.Width), Math.Min(8, bitmap.PixelSize.Height)));
                using var staging = new WriteableBitmap(sample.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using var frame = staging.Lock();
                sample.CopyPixels(frame);
                var pixels = new byte[sample.PixelSize.Width * sample.PixelSize.Height * 4];
                for (int y = 0; y < sample.PixelSize.Height; y++)
                    Marshal.Copy(frame.Address + y * frame.RowBytes, pixels, y * sample.PixelSize.Width * 4, sample.PixelSize.Width * 4);
                cached = FlashLuminance.Average(pixels, bgra: true);
                if (!string.IsNullOrEmpty(path))
                {
                    if (LuminanceCache.Count >= 500) LuminanceCache.Clear();
                    LuminanceCache[path] = cached;
                }
            }
            SetLuminance(cached * Math.Clamp(settings.LuminanceSyncIntensity, 0, 1), (int)Math.Clamp(lifetime.TotalMilliseconds, 100, 30000));
        }
        catch (Exception ex) { Log.Debug(ex, "Flash luminance sync failed"); }
    }
}
