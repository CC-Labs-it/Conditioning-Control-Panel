using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// A short one-colour wash across the whole virtual desktop that fades out (Pink Rush start).
///
/// <para>ccp-bugs #1415: this used to be a WPF window with AllowsTransparency the size of the
/// virtual desktop and an animated Opacity. That is a per-pixel layered surface: creating it
/// blocked the UI thread for over a second (1326 ms in the report's log) and every fade frame
/// re-uploaded every pixel of every monitor, so the whole screen dropped frames. The wash is
/// now a <see cref="NativeLayeredWindow"/>: painted once, faded by the desktop compositor.</para>
/// </summary>
internal static class ScreenWash
{
    /// <summary>The wash's peak, 0..1. The old window was a 100/255 fill at 0.6 opacity; this is
    /// the same strength in one number.</summary>
    internal const double PinkRushPeak = Models.PinkRushRules.WashPeak;

    internal const int PinkRushFadeMs = Models.PinkRushRules.WashFadeMs;

    /// <summary>Steps of the fade. 30 a second, the house effect clock.</summary>
    internal const int StepMs = Models.PinkRushRules.WashStepMs;

    /// <summary>Alpha (0..255) of the wash <paramref name="elapsedMs"/> into a fade of
    /// <paramref name="fadeMs"/> that starts at <paramref name="peak"/>: a straight line down to
    /// zero, never above the peak, never below zero.</summary>
    internal static byte AlphaAt(double elapsedMs, double fadeMs, double peak)
        => Models.PinkRushRules.WashAlphaAt(elapsedMs, fadeMs, peak);

    /// <summary>UI thread. Show the wash at its peak and fade it out. Never throws.</summary>
    public static void Flash(byte r, byte g, byte b, double peak, int fadeMs)
    {
        NativeLayeredWindow? win = null;
        try
        {
            win = new NativeLayeredWindow(r, g, b, clickThrough: true);
            if (win.Handle == IntPtr.Zero) { win.Dispose(); return; }

            win.SetAlpha(AlphaAt(0, fadeMs, peak));
            win.ShowAt(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

            var started = Environment.TickCount64;
            var w = win;
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(StepMs) };
            timer.Tick += (_, _) =>
            {
                var elapsed = Environment.TickCount64 - started;
                if (elapsed >= fadeMs)
                {
                    timer.Stop();
                    w.Dispose();
                    return;
                }
                w.SetAlpha(AlphaAt(elapsed, fadeMs, peak));
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            try { win?.Dispose(); } catch { /* best effort */ }
            App.Logger?.Debug("Screen wash failed: {Error}", ex.Message);
        }
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
