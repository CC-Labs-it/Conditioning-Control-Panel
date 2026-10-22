using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF ScreenWash (b86fa2aa6): paint once, fade with the desktop compositor's alpha.
/// One surface per screen avoids a virtual-desktop-sized texture. No Visual opacity animation.</summary>
internal static class PinkRushWash
{
    private static readonly HashSet<TintOverlayWindow> Active = new();
    internal static Func<long> Clock = () => Environment.TickCount64;
    internal static Action<TimeSpan, Action> After = (delay, action) => DispatcherTimer.RunOnce(action, delay);
    internal static Func<TintOverlayWindow, double, bool> SetAlpha = (window, alpha) => X11Overlay.SetOpacity(window, alpha);

    internal static void Show(Screen screen)
    {
        var window = new TintOverlayWindow();
        window.PlaceOn(screen);
        window.SetTint(0xFF, 0x14, 0x93, 1);
        if (!X11Overlay.SetClickThrough(window, true)) { window.Close(); return; }
        Start(window);
    }

    internal static void Start(TintOverlayWindow window)
    {
        if (!SetAlpha(window, PinkRushRules.WashAlphaAt(0, PinkRushRules.WashFadeMs, PinkRushRules.WashPeak) / 255.0))
        {
            window.Close();
            return;
        }
        Active.Add(window);
        window.Closed += (_, _) => Active.Remove(window);
        try { window.Show(); }
        catch { Active.Remove(window); window.Close(); throw; }
        var started = Clock();
        void Tick()
        {
            if (!Active.Contains(window)) return;
            var elapsed = Clock() - started;
            if (elapsed >= PinkRushRules.WashFadeMs ||
                !SetAlpha(window, PinkRushRules.WashAlphaAt(elapsed, PinkRushRules.WashFadeMs, PinkRushRules.WashPeak) / 255.0))
            {
                window.Close();
                return;
            }
            After(TimeSpan.FromMilliseconds(PinkRushRules.WashStepMs), Tick);
        }
        After(TimeSpan.FromMilliseconds(PinkRushRules.WashStepMs), Tick);
    }

    internal static void CloseAll()
    {
        foreach (var window in Active.ToArray()) window.Close();
    }
}
