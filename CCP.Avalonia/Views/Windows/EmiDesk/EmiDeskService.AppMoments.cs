using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

internal sealed partial class EmiDeskService
{
    private DispatcherTimer? _clockTimer;
    private string? _clockDay, _lateNightNight, _smallHoursNight, _morningDay;
    private bool _saidLongSitting, _saidIdleLong;
    private static readonly DateTime LaunchedUtc = ResolveLaunchUtc();
    internal Func<DateTime> ClockLocal = () => DateTime.Now;
    internal Func<DateTime> ClockUtc = () => DateTime.UtcNow;
    internal Func<int?> IdleSeconds = Platform.DesktopIdle.ReadSeconds;
    internal Func<Task<int?>> ReadIdleAsync = Platform.DesktopIdle.ReadSecondsAsync;
    private bool _clockPending;
    internal Action<string, object?>? ClockMoment;
    private AutonomyScheduler? _momentAutonomy;
    private SpeechEngine? _momentSpeech;
    private DailyFreeService? _momentDailyFree;
    private bool _eventsWired;
    private DateTime _takeoverSinceUtc = DateTime.MinValue;

    private void Moment(string name, object? context = null)
    {
        if (ClockMoment != null) ClockMoment(name, context);
        else Fire(name, context);
    }

    // WPF EmiDeskService.cs:1847: retain the exact event sources for shutdown.
    internal void WireAppEvents(AutonomyScheduler? autonomy, SpeechEngine? speech, DailyFreeService? dailyFree)
    {
        if (_eventsWired) return;
        _eventsWired = true;
        _momentAutonomy = autonomy;
        _momentSpeech = speech;
        _momentDailyFree = dailyFree;
        ProgressionBank.XPAwarded += OnXpAwarded;
        if (autonomy != null) autonomy.EnabledChanged += OnAutonomyEnabled;
        if (speech != null) speech.ListeningChanged += OnListeningChanged;
        if (dailyFree != null) dailyFree.TodayChanged += OnDailyFreeChanged;
        StartClock();
    }

    internal void UnwireAppEvents()
    {
        StopClock();
        if (!_eventsWired) return;
        ProgressionBank.XPAwarded -= OnXpAwarded;
        if (_momentAutonomy != null) _momentAutonomy.EnabledChanged -= OnAutonomyEnabled;
        if (_momentSpeech != null) _momentSpeech.ListeningChanged -= OnListeningChanged;
        if (_momentDailyFree != null) _momentDailyFree.TodayChanged -= OnDailyFreeChanged;
        _momentAutonomy = null;
        _momentSpeech = null;
        _momentDailyFree = null;
        _takeoverSinceUtc = DateTime.MinValue;
        _eventsWired = false;
    }

    private void OnXpAwarded(ProgressionBank.XpAward award)
    {
        try
        {
            if (award.LeveledUp) return;
            int amount = (int)Math.Round(award.Amount);
            if (amount >= 250) Moment("xpBigAward", new { n = amount, target = EmiNames.XpSource(award.Source) });
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] XP moment failed"); }
    }

    private void OnAutonomyEnabled(object? sender, bool enabled)
    {
        try
        {
            if (enabled) { _takeoverSinceUtc = ClockUtc(); Moment("takeoverStarted"); return; }
            int minutes = _takeoverSinceUtc == DateTime.MinValue ? 0
                : Math.Max(0, (int)(ClockUtc() - _takeoverSinceUtc).TotalMinutes);
            _takeoverSinceUtc = DateTime.MinValue;
            Moment("takeoverEnded", new { minutes });
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] takeover moment failed"); }
    }

    private void OnListeningChanged(object? sender, bool listening)
    {
        try { if (listening) Moment("sheListeningOn"); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] listening moment failed"); }
    }

    private void OnDailyFreeChanged()
    {
        try { Moment("dailyFreeToday", new { target = EmiNames.Feature(_momentDailyFree?.TodayKey) }); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] daily free moment failed"); }
    }

    private static DateTime ResolveLaunchUtc()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }

    internal void StartClock()
    {
        if (_clockTimer != null) return;
        try { if (EmiDebug.ResetOnboarding) EmiState.ResetOnboarding(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] onboarding reset failed"); }
        try { if (EmiDebug.ResetKnock) EmiState.ResetKnock(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] knock reset failed"); }
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
    }

    internal void StopClock()
    {
        if (_clockTimer == null) return;
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        _clockTimer = null;
    }

    private async void OnClockTick(object? sender, EventArgs e) => await ClockTickAsync(LaunchedUtc);

    internal async Task ClockTickAsync(DateTime launchedUtc)
    {
        var timer = _clockTimer;
        if (timer == null || !IsOut || _clockPending) return;
        _clockPending = true;
        try
        {
            int idle;
            try { idle = await ReadIdleAsync() ?? 0; } catch { idle = 0; }
            if (_clockTimer == timer && IsOut) ClockTick(launchedUtc, idle);
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] clock tick failed"); }
        finally { _clockPending = false; }
    }

    internal void ClockTick(DateTime launchedUtc)
    {
        if (!IsOut) return;
        int idle = 0;
        try { idle = IdleSeconds() ?? 0; } catch { }
        ClockTick(launchedUtc, idle);
    }

    private void ClockTick(DateTime launchedUtc, int idle)
    {
        if (!IsOut) return;
        var now = ClockLocal();
        var night = now.AddHours(-6).ToString("yyyy-MM-dd");
        var day = now.ToString("yyyy-MM-dd");
        if (_clockDay == null) _clockDay = day;
        else if (_clockDay != day) { _clockDay = day; Moment("dayTurned"); }
        if (now.Hour < 3 && _lateNightNight != night)
        { _lateNightNight = night; Moment("lateNight", new { n = now.Hour }); }
        else if (now.Hour >= 3 && now.Hour < 5 && _smallHoursNight != night)
        { _smallHoursNight = night; Moment("smallHours", new { n = now.Hour }); }
        else if (now.Hour >= 5 && now.Hour < 10 && _morningDay != day)
        { _morningDay = day; Moment("morningFirst"); }
        var open = ClockUtc() - launchedUtc;
        if (!_saidLongSitting && open.TotalHours >= 2)
        { _saidLongSitting = true; Moment("longSitting", new { minutes = (int)open.TotalMinutes }); }
        // WPF EmiDeskService.cs:2035-2059: a failed probe falls back to zero idle.
        if (idle >= 1200)
        {
            if (!_saidIdleLong) { _saidIdleLong = true; Moment("appIdleLong", new { minutes = idle / 60 }); }
        }
        else if (idle >= 300) Moment("idleShort", new { minutes = idle / 60 });
        else { _saidIdleLong = false; Moment("ambient"); }
    }
}
