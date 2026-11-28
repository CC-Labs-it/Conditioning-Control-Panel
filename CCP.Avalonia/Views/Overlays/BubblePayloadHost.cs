using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Notifications;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>Ambient bubble effects only; chaos payout owns the run path.</summary>
internal static class BubblePayloadHost
{
    private static readonly List<IDisposable> Waiting = new();
    internal static void Stop()
    {
        BubbleOverlay.Payloads.Stop();
        foreach (var timer in Waiting) timer.Dispose();
        Waiting.Clear();
        StopMelt();
    }

    // WPF EffectPayloadFiring.cs:71-102: the Brain Drain bubble's timed drain. On X11 this head has no capture
    // that leaves out its own overlay windows, so the drain is the milky veil FirstShowEffects.cs:89-99 uses
    // (docs/avalonia-decisions.html#2026-11-24-bubble-brain-drain-veil).
    private static readonly List<TintOverlayWindow> MeltWindows = new();
    private static int _meltGeneration;
    internal static bool MeltActive { get; private set; }
    internal static IReadOnlyList<TintOverlayWindow> Melt => MeltWindows;
    internal static Func<bool> UserDrainUp = () => Platform.BrainDrainPlayer.Instance?.IsRunning == true;
    internal static bool SkipPlatformChecksForTests;

    internal static void BrainDrainMelt(Visual host)
    {
        if (!BrainDrainBubble.ShouldPlayOverlay(MeltActive, UserDrainUp()))
        { Log.Information("Bubble: brain drain pop paid XP only - a drain is already up"); return; }
        var strength = CoreSettings.Current.BrainDrainBlurStrength;
        if (BrainDrainVisualPolicy.IsSilent(strength))
        { Log.Information("Bubble: brain drain pop paid XP only - the blur dial is at 0"); return; }
        if (!SkipPlatformChecksForTests && !Platform.X11Overlay.IsAvailable) return;
        MeltActive = true;
        var generation = ++_meltGeneration;
        // FirstShowEffects.cs:89: 70/255 at the dial's 70, so the veil scales the same way.
        var alpha = BrainDrainBubble.OverlayOpacity(strength) * 0.4;
        // WPF Compositor/BrainDrainLayer.cs:397-403 / Notifications/OverlayService.cs:2407-2414: only the "Show content on" screens.
        var screens = Features.ScreenList.Enumerate(host);
        var s = CoreSettings.Current;
        var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
        foreach (var i in PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary))
        {
            var w = new TintOverlayWindow();
            w.PlaceOn(screens[i]);
            w.SetTint(236, 228, 244, alpha);
            // Gentle visuals: the veil rises over the Gentle fade instead of appearing at once (PinkFilterOverlay's rule).
            var gentle = ConditioningControlPanel.Services.Safety.GentleVisuals.On;
            if (gentle) w.Opacity = 0;
            w.Show();
            MeltWindows.Add(w);
            if (gentle)
            {
                w.Transitions = new global::Avalonia.Animation.Transitions { new global::Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(ConditioningControlPanel.Services.Safety.GentleVisuals.FadeSeconds) } };
                Dispatcher.UIThread.Post(() => { if (MeltWindows.Contains(w)) w.Opacity = 1; });
            }
            if (!SkipPlatformChecksForTests && (!Platform.X11Overlay.SetClickThrough(w, true)
                || w.ActualTransparencyLevel == WindowTransparencyLevel.None)) { StopMelt(); return; }
        }
        Log.Debug("Bubble: brain drain {Kind} veil for {Ms} ms", BrainDrainBubble.OverlayKindFor(CoreSettings.Current.MotionLevel), BrainDrainBubble.OverlayMs);
        DispatcherTimer.RunOnce(() => { if (generation == _meltGeneration) StopMelt(); }, TimeSpan.FromMilliseconds(BrainDrainBubble.OverlayMs));
    }

    private static void StopMelt()
    {
        _meltGeneration++;
        MeltActive = false;
        foreach (var w in MeltWindows)
        {
            try { w.Close(); } catch (Exception ex) { Log.Debug("Bubble: melt close failed: {E}", ex.Message); }
        }
        MeltWindows.Clear();
    }

    internal static void Run(EffectPayload payload) => BubbleOverlay.Payloads.Run(payload,
        () => BubbleOverlay.IsRunning, () => CoreEngine.Video?.IsPlaying == true,
        () => Chaos.ChaosGifCascadeOverlay.IsRaining,
        a => Dispatcher.UIThread.Post(a, DispatcherPriority.Background), WaitForRain, p => Fire(p));

    private static void WaitForRain(Action action)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        IDisposable? timer = null;
        timer = DispatcherTimer.Run(() =>
        {
            if (DateTime.UtcNow >= deadline) { Waiting.Remove(timer!); return false; }
            if (Chaos.ChaosGifCascadeOverlay.IsRaining) return true;
            Waiting.Remove(timer!);
            action();
            return false;
        }, TimeSpan.FromMilliseconds(100));
        Waiting.Add(timer);
    }

    internal static void Fire(EffectPayload payload)
    {
        var host = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (host == null && payload is FlashPayload or OverlayPayload or BrainDrainMeltPayload) return;
        switch (payload)
        {
            case FlashPayload p:
                var (amount, duration, size) = p.ResolveBurst(CoreSettings.Current);
                FlashOverlay.TriggerOnce(host!, amount, duration, size, ambient: p.Ambient);
                break;
            case SubliminalPayload:
                if (CoreSubliminal.PickPhrase() is { } phrase) CoreSubliminal.ShowProvider?.Invoke(phrase);
                break;
            case BambiFreezePayload: CoreSubliminal.TriggerBambiFreeze(); break;
            case OverlayPayload p when p.OverlayKind == "braindrain":
                if (p.BraindrainOpacity is { } opacity)
                    ChaosFlashOverlay.Show(host!, (int)(p.Scale(1500, 4500) * EffectPayload.GlobalDurationMult * p.DurationMult), opacity);
                else ChaosFlashOverlay.Show(host!);
                break;
            case OverlayPayload p:
                var ms = (int)(p.Scale(1500, 4500) * EffectPayload.GlobalDurationMult * p.DurationMult);
                if (p.OverlayKind == "pink_filter") PinkFilterOverlay.ShowTimed(host!, ms, p.ScaleD(0.25, 0.70));
                else if (p.OverlayKind == "spiral") SpiralOverlay.ShowTimed(host!, ms, p.ScaleD(0.25, 0.70));
                break;
            // WPF EffectPayload.cs:250 / VideoService.cs:2311: a popped bubble is user-earned, so a
            // visible For You feed does not hold it (only scheduled and other non-earned videos wait).
            case BrainDrainMeltPayload: BrainDrainMelt(host!); break;
            case VideoPayload: CoreEngine.Video?.Trigger(silentIfEmpty: true, userEarned: true); break;
            case GifCascadePayload:
                Chaos.ChaosGifCascadeOverlay.Show(GifCascadePayload.SPAWN_RATE_PER_SEC,
                    GifCascadePayload.DURATION_SEC * EffectPayload.GlobalDurationMult,
                    GifCascadePayload.GIF_SIZE, GifCascadePayload.FALL_SPEED, GifCascadePayload.OPACITY, GifCascadePayload.START_SCALE);
                break;
            default:
                // ponytail: whisper, HT link and bouncing-text payloads
                // need their own one-shot hosts. Do not mutate the user's persistent effect settings.
                Log.Warning("Bubble: {Payload} has no one-shot host yet", payload.DisplayName);
                break;
        }
    }
}
