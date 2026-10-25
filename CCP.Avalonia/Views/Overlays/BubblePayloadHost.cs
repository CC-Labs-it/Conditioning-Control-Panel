using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
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
    }

    internal static void Run(EffectPayload payload) => BubbleOverlay.Payloads.Run(payload,
        () => BubbleOverlay.IsRunning, () => CoreEngine.Video?.IsPlaying == true,
        () => Chaos.ChaosGifCascadeOverlay.IsRaining,
        a => Dispatcher.UIThread.Post(a, DispatcherPriority.Background), WaitForRain, Fire);

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
        if (host == null) return;
        switch (payload)
        {
            case FlashPayload p:
                var (amount, duration, size) = p.ResolveBurst(CoreSettings.Current);
                FlashOverlay.TriggerOnce(host, amount, duration, size);
                break;
            case SubliminalPayload:
                if (CoreSubliminal.PickPhrase() is { } phrase) CoreSubliminal.ShowProvider?.Invoke(phrase);
                break;
            case BambiFreezePayload: CoreSubliminal.TriggerBambiFreeze(); break;
            case OverlayPayload p when p.OverlayKind == "braindrain":
                if (p.BraindrainOpacity is { } opacity)
                    ChaosFlashOverlay.Show(host, (int)(p.Scale(1500, 4500) * EffectPayload.GlobalDurationMult * p.DurationMult), opacity);
                else ChaosFlashOverlay.Show(host);
                break;
            case OverlayPayload p:
                var ms = (int)(p.Scale(1500, 4500) * EffectPayload.GlobalDurationMult * p.DurationMult);
                if (p.OverlayKind == "pink_filter") PinkFilterOverlay.ShowTimed(host, ms, p.ScaleD(0.25, 0.70));
                else if (p.OverlayKind == "spiral") SpiralOverlay.ShowTimed(host, ms, p.ScaleD(0.25, 0.70));
                break;
            case VideoPayload: CoreEngine.Video?.Trigger(); break;
            case GifCascadePayload:
                Chaos.ChaosGifCascadeOverlay.Show(GifCascadePayload.SPAWN_RATE_PER_SEC,
                    GifCascadePayload.DURATION_SEC * EffectPayload.GlobalDurationMult,
                    GifCascadePayload.GIF_SIZE, GifCascadePayload.FALL_SPEED, GifCascadePayload.OPACITY, GifCascadePayload.START_SCALE);
                break;
            default:
                // ponytail: melt, whisper, HT link and bouncing-text payloads
                // need their own one-shot hosts. Do not mutate the user's persistent effect settings.
                Log.Warning("Bubble: {Payload} has no one-shot host yet", payload.DisplayName);
                break;
        }
    }
}
