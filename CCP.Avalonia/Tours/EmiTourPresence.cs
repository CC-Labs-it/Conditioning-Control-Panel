using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Tours;

/// <summary>
/// WPF Services/EmiDesk/EmiTourNarrator.cs: summon EMI for a tour (:119-145), then narrate it as
/// moments on her one bus (<see cref="EmiDeskService.Fire"/>): <c>tourStarted</c>, <c>tour.&lt;stepId&gt;</c>
/// or <c>tourStep</c>, <c>tourFinished</c> / <c>tourSkipped</c>. Narration is additive and never
/// load-bearing: a missing, muted or throwing desk leaves the tour exactly as it is.
/// The book offer after the ending (WPF <c>EmiCodex.MaybeOfferSoon</c>) is not on this head yet.
/// </summary>
internal sealed class EmiTourPresence : IDisposable
{
    private readonly TutorialService _service;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _isOut;
    private readonly Func<Task> _summon;
    private readonly Action<string> _fire;
    private readonly Func<string, bool> _hasMoment;
    private bool _disposed;
    // The tour this attachment narrates: a step or an ending from another run is not ours.
    private bool _narrating;
    internal Task PendingSummon { get; private set; } = Task.CompletedTask;

    internal static EmiTourPresence? Active { get; private set; }

    // WPF EmiTourNarrator.cs:61-88: one attachment, idempotent for the same service.
    internal static void Attach(TutorialService service)
    {
        if (Active is { _disposed: false } current && ReferenceEquals(current._service, service)) return;
        Detach();
        Active = new EmiTourPresence(service,
            () => CoreSettings.Current.EmiDeskEnabled,
            () => EmiDeskService.Instance.IsOut,
            () => EmiDeskService.Instance.Summon("tour"),
            moment => EmiDeskService.Instance.Fire(moment),
            id => EmiLineEngine.Instance.MomentIds.Contains(id, StringComparer.Ordinal));
    }

    internal static void Detach()
    {
        Active?.Dispose();
        Active = null;
    }

    internal EmiTourPresence(TutorialService service, Func<bool> enabled, Func<bool> isOut, Func<Task> summon,
        Action<string>? fire = null, Func<string, bool>? hasMoment = null)
    {
        _service = service;
        _enabled = enabled;
        _isOut = isOut;
        _summon = summon;
        _fire = fire ?? (_ => { });
        _hasMoment = hasMoment ?? (_ => false);
        _service.TutorialStarted += OnStarted;
        _service.StepChanged += OnStepChanged;
        _service.TutorialFinished += OnFinished;
    }

    private void OnStarted(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _narrating = true;
        PendingSummon = SummonThenGreet();
    }

    private async Task SummonThenGreet()
    {
        try
        {
            // WPF :133-136. TutorialHead awaits this task before activating the first card.
            if (!_isOut() && _enabled()) await _summon();
        }
        catch (Exception ex)
        {
            // WPF :141-145: EMI is additive; a failure must leave the tour running.
            Log.Debug(ex, "[EmiDesk] tour summon failed");
        }
        // WPF :138: summon first, then speak (an away desk drops the line).
        if (!_disposed && _narrating) Speak("tourStarted");
    }

    // WPF :148-176: the step's own pool, else the generic step moment.
    private void OnStepChanged(object? sender, TutorialStep step)
    {
        if (_disposed || !_narrating || string.IsNullOrWhiteSpace(step?.Id)) return;
        var moment = "tour." + step.Id;
        bool own;
        try { own = _hasMoment(moment); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour moment lookup failed"); own = false; }
        Speak(own ? moment : "tourStep");
    }

    // WPF :178-193. Not gated on _disposed: the overlay can close (and detach this) inside the same
    // Finished dispatch, before this handler runs. A panic ending stays silent (WPF arms the
    // panicPressed hold before abandoning the tour, MainWindow.xaml.cs:1617-1620).
    private void OnFinished(object? sender, TutorialFinishedEventArgs e)
    {
        if (!_narrating) return;
        _narrating = false;
        if (TutorialHead.EndingInstantly) return;
        Speak(e.Completed ? "tourFinished" : "tourSkipped");
    }

    private void Speak(string moment)
    {
        try { _fire(moment); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour narration {Moment} failed", moment); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.TutorialStarted -= OnStarted;
        _service.StepChanged -= OnStepChanged;
        _service.TutorialFinished -= OnFinished;
    }
}
