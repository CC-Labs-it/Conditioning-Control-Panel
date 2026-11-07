using System;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// One clock for every "Online media today" surface on this head (WPF MainWindow.OnlineMeter.cs WireOnlineMeter): the
/// title-bar pill and the Assets card repaint from <see cref="Now"/> on <see cref="Refresh"/>, which fires (UI thread)
/// at most once a second on a stream of charges (<see cref="OnlineMeterRules.Throttle"/>), every minute (the "back in
/// 6 h" line counts down), and when a surface pokes it (the media source changed, the official note was dismissed).
/// What shows is decided by Core <see cref="OnlineMeterRules"/>, shared with WPF. Percent only, never MB.
/// </summary>
internal static class OnlineMeterHead
{
    private static DispatcherTimer? _throttle, _clock;

    /// <summary>Raised on the UI thread when the surfaces should repaint.</summary>
    internal static event Action? Refresh;

    /// <summary>The meter's state right now, from the live allowance, the switch and the settings.</summary>
    internal static OnlineMeterState Now()
    {
        var s = CoreSettings.Current;
        return OnlineMeterRules.For(s?.MediaSource, s?.HasRemoteMediaConsent == true, ScrolllerPolicy.Enabled,
            ScrolllerAllowance.Percent, ScrolllerAllowance.Spent, ScrolllerAllowance.Plan, ScrolllerPolicy.Official,
            s?.ScrolllerOfficialNoteSeen == true);
    }

    /// <summary>Hooks the allowance and starts the minute clock. Idempotent; UI thread.</summary>
    internal static void Start()
    {
        if (_clock != null) return;
        _throttle = new DispatcherTimer { Interval = OnlineMeterRules.Throttle };
        _throttle.Tick += (_, _) => { _throttle.Stop(); Raise(); };
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _clock.Tick += (_, _) => Raise();
        _clock.Start();
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        ScrolllerAllowance.Changed += (_, _) => Dispatcher.UIThread.Post(() => { if (_throttle is { IsEnabled: false } t) t.Start(); });
    }

    /// <summary>A surface changed something the meter reads (source, consent, the note): repaint now.</summary>
    internal static void Poke() => Dispatcher.UIThread.Post(Raise);

    private static void OnLanguageChanged(object? sender, EventArgs e) => Poke();

    private static void Raise()
    {
        try { Refresh?.Invoke(); }
        catch (Exception ex) { Log.Debug("Online meter refresh failed: {E}", ex.Message); }
    }

    /// <summary>The pill's text (WPF PaintOnlineMeterPill): "Online 42%", or "Online done for today" once spent.</summary>
    internal static string PillText(OnlineMeterState st) => st.Spent
        ? Loc.Get("online_pill_done")
        : string.Format(Loc.Get("online_pill"), st.Percent);

    /// <summary>The card's line under the bar (WPF PaintOnlineMeterCard): the plan line, or "used up, back in 6 h",
    /// then the upsell when there is one.</summary>
    internal static string CardLine(OnlineMeterState st, DateTime now, DateTime reset)
    {
        string head;
        if (st.Spent)
        {
            var (key, value) = OnlineMeterRules.BackIn(now, reset);
            head = string.Format(Loc.Get("online_meter_spent"), string.Format(Loc.Get(key), value));
        }
        else head = Loc.Get(st.UnderKey);
        return st.UpsellKey == null ? head : head + "   " + Loc.Get(st.UpsellKey);
    }
}
