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
            var engine = LineEngine;
            if (!IsOut)
            {
                if (engine.IsHoldMoment(moment)) engine.Draw(moment, EmiLineEngine.ToCtx(context));
                return;
            }
            // WPF :677-689: safety holds arm even while the widget is away.
            if (_window?.PresentationActive == true && !engine.IsHoldMoment(moment)) return;
            var ctx = EmiLineEngine.ToCtx(context);
            var line = engine.Draw(moment, ctx);
            // WPF EmiDeskService.cs:649 NeverSpeaks; a hold is a face, so it plays even there (:765).
            bool neverSpeaks = moment is "dismissed" or "appClosing";
            if (line != null && (line.Hold || !neverSpeaks))
                _window?.SpeakLine(line, engine);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] speech moment {Moment} failed", moment); }
    }
    private void ScheduleGreeting()
    {
        CancelGreeting();
        _greetingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2600)
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
        // WPF EmiDeskService.cs:819,847-855: delayed, ordered greetings.
        var moment = !LineEngine.EverSpent("firstContact") ? "firstContact"
            : !LineEngine.EverSpent("desktopFirstBoot") ? "desktopFirstBoot" : "summoned";
        Fire(moment, new { via = "rail", minutes = 0, touring = false });
    }
    private void CancelGreeting()
    {
        _greetingTimer?.Stop();
        _greetingTimer = null;
    }
    internal void PanicSilence()
    {
        CancelGreeting();
        _window?.CancelChain();
        Fire("panicPressed");
    }
}
