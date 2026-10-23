using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

internal sealed partial class EmiDeskService
{
    private DispatcherTimer? _greetingTimer;
    internal EmiLineEngine LineEngine { get; set; } = EmiLineEngine.Instance;

    // WPF Fire/Speak: the Core engine owns spice, cadence, limits and holds. The window owns pixels.
    public void Fire(string moment, object? context = null)
    {
        if (string.IsNullOrWhiteSpace(moment)) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Fire(moment, context));
            return;
        }
        try
        {
            // WPF Bubble.cs:869 OnMomentForAsk: a screen-taking moment kills a waiting offer.
            if (EmiDeskWindow.AskKillMoments.Contains(moment)) _window?.CancelAsk(moment);
            var engine = LineEngine;
            if (!IsOut && !engine.IsHoldMoment(moment)) return;
            if (_window?.PresentationActive == true) return;
            var ctx = EmiLineEngine.ToCtx(context);
            var line = engine.Draw(moment, ctx);
            var ask = engine.DrawAsk(moment, ctx);
            // WPF EmiDeskService.cs:649 NeverSpeaks; a hold is a face, so it plays even there (:765).
            bool neverSpeaks = moment is "dismissed" or "appClosing";
            if (ask != null)
            {
                if (!neverSpeaks) _window?.ShowAsk(ask, engine);
                return;
            }
            if (line != null && (line.Hold || !neverSpeaks))
                _window?.SpeakLine(line, engine);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] speech moment {Moment} failed", moment); }
    }

    // WPF App.xaml.cs:375-376 seeds both probes once the desk service exists.
    private EmiDeskService()
    {
        EmiLineEngine.AskSituationProbe = AskSituationOk;
        EmiLineEngine.EffectFeasibleProbe = EmiOffers.EffectFeasible;
    }

    /// <summary>WPF EmiDeskService.AskSituationOk: no offer while anything else owns the screen.</summary>
    internal bool AskSituationOk()
    {
        try
        {
            if (!IsOut) return false;
            var win = _window;
            if (win == null || !win.IsVisible) return false;
            if (win.PresentationActive || win.InputLocked || win.Transiting || win.AskLive) return false;
            if (Views.Overlays.MandatoryVideoOverlay.Instance.Scheduler.IsPlaying) return false;
            if (CoreSession.IsSessionRunning) return false;
            if (Views.AvatarTube.AvatarTubeWindow.Live?.HasBubbleUp == true) return false;
            var main = (global::Avalonia.Application.Current?.ApplicationLifetime
                as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            return main?.WindowState != global::Avalonia.Controls.WindowState.Minimized;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ask situation probe failed");
            return false;
        }
    }

    private void ScheduleGreeting(string? why)
    {
        CancelGreeting();
        _summonVia = string.Equals(why, "hotkey", StringComparison.OrdinalIgnoreCase) ? "hotkey" : "rail";
        _summonTouring = string.Equals(why, "tour", StringComparison.OrdinalIgnoreCase);
        _greetingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(EmiSpeechCadence.SummonGreetingMs)
        };
        _greetingTimer.Tick += (_, _) => GreetingDue();
        _greetingTimer.Start();
    }

    // The timer and the headless tests drive the same step: nothing greets unless Summon scheduled it.
    internal void GreetingDue()
    {
        if (_greetingTimer != null) GreetNow();
    }

    internal void GreetNow()
    {
        CancelGreeting();
        if (!IsOut) return;
        var moment = EmiSpeechCadence.Greeting(LineEngine.EverSpent("firstContact"), LineEngine.EverSpent("desktopFirstBoot"));
        var mute = _pendingMuteMoment;
        _pendingMuteMoment = null;
        if (mute != null) Fire(mute);
        ScheduleEmptyLibrary();
        Fire(moment, new { via = _summonVia, minutes = MinutesOut(), touring = _summonTouring });
        _summonTouring = false;
    }

    private void CancelGreeting()
    {
        _greetingTimer?.Stop();
        _greetingTimer = null;
    }

    internal void PanicSilence()
    {
        CancelGreeting();
        CancelFollowups();
        Fire("panicPressed");
        _window?.CancelChain();
    }
}
