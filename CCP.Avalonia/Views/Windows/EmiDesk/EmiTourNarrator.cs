using System;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

/// <summary>
/// EMI narrates a shell tour. PORTED from <c>ConditioningControlPanel/Services/EmiDesk/EmiTourNarrator.cs</c>:
/// summon-then-<c>tourStarted</c>, one <c>tour.&lt;stepId&gt;</c> moment per step (falling back to
/// <c>tourStep</c>), and <c>tourFinished</c>/<c>tourSkipped</c> at the end. Attached by
/// <see cref="TutorialHead.StartOnShell"/> (WPF MainWindow.Settings.cs:570) and detached when the
/// overlay closes (:577). Narration is additive: a narrator that throws never takes the tour with it.
/// ponytail: the book offer after the ending line (WPF EmiCodex.MaybeOfferSoon) waits for the codex
/// offer path on this head (wire-views-settings-emidesk-b).
/// </summary>
internal sealed class EmiTourNarrator : IDisposable
{
    private readonly TutorialService _service;
    private bool _disposed;
    private bool _narrating;

    public static EmiTourNarrator? Active { get; private set; }

    public static void Attach(TutorialService? service)
    {
        if (service == null) return;
        try
        {
            if (Active != null && ReferenceEquals(Active._service, service) && !Active._disposed) return;
            Detach();
            Active = new EmiTourNarrator(service);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour narrator could not attach"); }
    }

    public static void Detach()
    {
        try { Active?.Dispose(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour narrator could not detach"); }
        finally { Active = null; }
    }

    public EmiTourNarrator(TutorialService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.TutorialStarted += OnStarted;
        _service.StepChanged += OnStepChanged;
        _service.TutorialFinished += OnFinished;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.TutorialStarted -= OnStarted;
        _service.StepChanged -= OnStepChanged;
        _service.TutorialFinished -= OnFinished;
    }

    private void OnStarted(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _narrating = true;
        try
        {
            var desk = EmiDeskService.Instance;
            // Summon FIRST, then speak: a moment nobody is out for is dropped. Summon sets IsOut
            // before its first await, so the line below already finds her out.
            if (!desk.IsOut && CoreSettings.Current.EmiDeskEnabled) _ = desk.Summon("tour");
            desk.Fire(EmiTourMoments.Started);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tourStarted narration failed"); }
    }

    private void OnStepChanged(object? sender, TutorialStep step)
    {
        if (_disposed || !_narrating) return;
        try
        {
            var desk = EmiDeskService.Instance;
            var moment = EmiTourMoments.Step(step?.Id, desk.LineEngine.MomentIds);
            if (moment != null) desk.Fire(moment);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] step narration failed for {Step}", step?.Id); }
    }

    private void OnFinished(object? sender, TutorialFinishedEventArgs e)
    {
        // Not gated on _disposed: this head's overlay closes synchronously on the ending (WPF fades
        // 200ms first), so the detach lands mid-event, before this handler runs. _narrating still
        // says the ending is ours; a narrator unsubscribed before the ending never gets here.
        if (!_narrating) return;
        _narrating = false;
        try { EmiDeskService.Instance.Fire(EmiTourMoments.Ending(e.Completed)); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour ending narration failed"); }
    }
}
