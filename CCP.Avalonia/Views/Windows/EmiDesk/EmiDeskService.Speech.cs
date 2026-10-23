using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

internal sealed partial class EmiDeskService
{
    private DispatcherTimer? _greetingTimer;
    private string _summonVia = "rail";
    // WPF EmiDeskService.cs:804: a tour summon greets with touring=true, so a firstContact offer
    // gated `!touring` never offers the tour she is already giving.
    private bool _summonTouring;
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
            var engine = LineEngine;
            if (!IsOut && !engine.IsHoldMoment(moment)) return;
            if (_window?.PresentationActive == true) return;
            var line = engine.Draw(moment, EmiLineEngine.ToCtx(context));
            // ponytail: asks/effect execution and glass channels are wire-views-settings-emidesk-b.
            // No feasibility probe is installed until those chips have real effects.
            // WPF EmiDeskService.cs:765: a hold is a face, so it plays even on a locked-silent moment.
            if (line != null && (line.Hold || moment is not "dismissed" and not "appClosing"))
                _window?.SpeakLine(line, engine);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] speech moment {Moment} failed", moment); }
    }

    private void ScheduleGreeting()
    {
        CancelGreeting();
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
        bool touring = _summonTouring;
        _summonTouring = false;
        if (!IsOut) return;
        var moment = EmiSpeechCadence.Greeting(LineEngine.EverSpent("firstContact"), LineEngine.EverSpent("desktopFirstBoot"));
        Fire(moment, new { via = _summonVia, minutes = 0, touring });
    }

    private void CancelGreeting()
    {
        _greetingTimer?.Stop();
        _greetingTimer = null;
    }

    internal void PanicSilence()
    {
        CancelGreeting();
        Fire("panicPressed");
        _window?.CancelChain();
    }
}
