using System;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF Services/Chaos/EffectPayloadFiring.cs:177-220. One payload owns one temporary
// use of the existing renderer. Stop/panic releases the timer before another owner starts.
internal static class ChaosBouncingTextEffect
{
    internal static TimeProvider Clock = TimeProvider.System;
    private static DispatcherTimer? _life;
    private static long _started;
    private static int _bounces;
    internal static double Duration { get; private set; }
    internal static bool IsActive => _life != null;

    internal static void Start(Visual host)
    {
        if (BouncingTextOverlay.IsRunning) return;
        var enabled = CoreSettings.Current.BouncingTextPool?.Where(p => p.Value).Select(p => p.Key).ToList();
        var pool = enabled is { Count: > 0 } ? new[] { enabled[Random.Shared.Next(enabled.Count)] } : null;
        BouncingTextOverlay.Start(host, pool);
        if (!BouncingTextOverlay.IsRunning) return;
        _bounces = 0;
        _started = Clock.GetTimestamp();
        Duration = Math.Max(0.5, BouncingTextPayload.DURATION_SEC * EffectPayload.GlobalDurationMult);
        _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Duration) };
        _life.Tick += (_, _) => CheckExpiry();
        BouncingTextOverlay.Bounced += OnBounce;
        BouncingTextOverlay.Stopped += Release;
        _life.Start();
    }

    internal static void CheckExpiry()
    {
        if (IsActive && Clock.GetElapsedTime(_started).TotalSeconds >= Duration) BouncingTextOverlay.Stop();
    }

    private static void OnBounce()
    {
        if (++_bounces >= BouncingTextPayload.MAX_BOUNCES) BouncingTextOverlay.Stop();
    }

    private static void Release()
    {
        _life?.Stop();
        _life = null;
        BouncingTextOverlay.Bounced -= OnBounce;
        BouncingTextOverlay.Stopped -= Release;
    }
}
