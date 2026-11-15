using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF OverlayService.cs:3047-3107, 3295-3328: keep session effects below video.
internal static class OverlayZOrder
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static bool _hadAnchor;
    internal static bool Running => Timer.IsEnabled;
    static OverlayZOrder() => Timer.Tick += (_, _) => Tick();
    internal static void Start() => Timer.Start();
    internal static void Stop() { Timer.Stop(); _hadAnchor = false; }

    private static int Monitor(Window w)
    {
        var screen = w.Screens.ScreenFromWindow(w);
        if (screen == null) return -1;
        var all = w.Screens.All;
        for (var i = 0; i < all.Count; i++) if (all[i].Bounds == screen.Bounds) return i;
        return -1;
    }

    internal static void Tick()
    {
        var videos = MandatoryVideoOverlay.Instance.Windows;
        var bubbles = BubbleCountWindow.OpenWindows.Cast<Window>().ToArray();
        var effects = PinkFilterOverlay.Shown.Cast<Window>()
            .Concat(SpiralOverlay.Shown)
            .Concat(CornerGifOverlay.ShownSlots.Select(CornerGifOverlay.WindowFor).OfType<Window>())
            .Concat(BrainDrainOverlay.Active);
        Reconcile(effects, videos, bubbles, BubbleCountHost.Instance.Scheduler.HoldsEffects,
            DisplayChangeCoordinator.RenderQuiesced, Monitor,
            w => w.Screens.ScreenFromWindow(w)?.IsPrimary == true,
            (w, a) => X11Overlay.RestackBelow(w, a), w => X11Overlay.Raise(w),
            BrainDrainOverlay.SinkKeepClear, ref _hadAnchor);
    }

    internal static void Reconcile(IEnumerable<Window> effects, IReadOnlyList<Window> videos,
        IReadOnlyList<Window> bubbles, bool holdsEffects, bool quiesced, Func<Window, int> monitor,
        Func<Window, bool> primary, Action<Window, Window> below, Action<Window> raise,
        Action sinkKeepClear, ref bool hadAnchor)
    {
        if (quiesced) return;
        var anchors = videos.Count > 0 ? videos : holdsEffects ? bubbles : Array.Empty<Window>();
        var fallback = anchors.FirstOrDefault(primary) ?? anchors.FirstOrDefault();
        if (anchors.Count > 0)
            Pin(effects, anchors, fallback, monitor, below);
        else if (hadAnchor)
            foreach (var effect in effects) raise(effect); // WPF :3396-3406, after a top window closes.
        hadAnchor = anchors.Count > 0;
        sinkKeepClear(); // WPF :3199-3201: keep-clear wins after every pin or raise.
    }

    // Seams for headless tests; the production path supplies the real windows and native call.
    internal static void Pin(IEnumerable<Window> effects, IReadOnlyList<Window> anchors, Window? fallback,
        Func<Window, int> monitor, Action<Window, Window> below)
    {
        var mapped = anchors.Select(w => (Window: w, Monitor: monitor(w))).ToArray();
        foreach (var effect in effects)
        {
            var anchor = OverlayZOrderPolicy.ResolveVideoAnchor(mapped, effect, monitor(effect), fallback);
            if (anchor != null) below(effect, anchor);
        }
    }
}
