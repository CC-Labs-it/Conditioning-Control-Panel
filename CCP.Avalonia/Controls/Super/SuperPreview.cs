using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls.Super;

/// <summary>
/// The weekly free Super try (WPF Services/Super/SuperPreview.cs): a free player runs THIS week's
/// effect for 10 s, once a week. Core <see cref="SuperAccess.TryingProvider"/> reads <see cref="Trying"/>
/// (seeded in App.Initialize). Panic and the emergency exit end it with a cut (PanicSurfaces
/// 'super-preview', EmergencyExitWindow.Open).
/// </summary>
public static class SuperPreview
{
    private static DispatcherTimer? _timer;
    private static SuperEffect? _trying;
    private static long _startedTick;

    private static double Elapsed => (Environment.TickCount64 - _startedTick) / 1000.0;

    /// <summary>The effect being tried, or null. Past the try plus its grace this reads null even if the
    /// end timer is late, so a stalled UI thread never stretches a free try.</summary>
    public static SuperEffect? Trying
        => _trying is SuperEffect e && SuperPreviewRule.TryStillRunning(Elapsed) ? e : null;

    /// <summary>The last try was cut by panic or the emergency exit, not run out.</summary>
    public static bool EndedByPanic { get; private set; }

    /// <summary>Raised on the UI thread when a try starts or ends.</summary>
    public static event Action? StateChanged;

    private static DateTimeOffset Now => ServerClock.UtcNow;
    private static int UsedWeek => CoreSettings.Service?.Current?.SuperPreviewUsedWeek ?? SuperPreviewRule.NeverUsed;

    public static SuperEffect ThisWeek => SuperPreviewRule.EffectThisWeek(Now);
    public static bool UsedThisWeek => SuperPreviewRule.UsedThisWeek(UsedWeek, Now);
    public static double SecondsLeft => Trying == null ? 0 : SuperPreviewRule.SecondsLeftAfter(Elapsed);

    /// <summary>Only an effect this head hosts (<see cref="SuperBox.HostedOnThisHead"/>), so no door ever
    /// spends the week's try on an effect that cannot draw here (owner call 2026-11-25: none for Undertow).</summary>
    public static bool CanTry(SuperEffect effect)
        => Trying == null && !TierGate.HasPremium && SuperBox.HostedOnThisHead.Contains(effect)
           && SuperPreviewRule.CanTry(effect, UsedWeek, Now);

    /// <summary>Start this week's try. False when the rule refuses (wrong effect, used, paid tier).</summary>
    public static bool TryStart(SuperEffect effect)
    {
        if (!CanTry(effect)) return false;
        var s = CoreSettings.Service?.Current;
        if (s == null) return false;
        // Spend the try BEFORE the effect starts, so a crash mid-try cannot hand out a second one.
        s.SuperPreviewUsedWeek = SuperPreviewRule.WeekIndex(Now);
        CoreSettings.Save();

        _trying = effect;
        _startedTick = Environment.TickCount64;
        EndedByPanic = false;
        Log.Information("SuperPreview: weekly try of {Effect} started", effect);
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SuperPreviewRule.TrySeconds) };
        _timer.Tick += (_, _) => End(panic: false);
        _timer.Start();
        StateChanged?.Invoke();
        SuperAccess.RaiseChanged(effect);
        return true;
    }

    /// <summary>End the running try; <paramref name="panic"/> = a cut, not a fade. Safe when idle.</summary>
    public static void End(bool panic)
    {
        _timer?.Stop();
        _timer = null;
        if (_trying is not SuperEffect effect) return;
        _trying = null;
        EndedByPanic = panic;
        Log.Information("SuperPreview: weekly try of {Effect} ended ({How})", effect, panic ? "panic" : "time");
        StateChanged?.Invoke();
        SuperAccess.RaiseChanged(effect);
    }
}
