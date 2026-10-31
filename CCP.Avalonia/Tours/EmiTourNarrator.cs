using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Tours;

// WPF Services/EmiDesk/EmiTourNarrator.cs:61-113,148-193.
// Summon/startup ordering belongs to PR #3512. This slice speaks only while EMI is out.
internal sealed class EmiTourNarrator : IDisposable
{
    private readonly TutorialService _service;
    private readonly Action<string> _fire;
    private readonly Func<string, bool> _hasMoment;
    private bool _narrating;
    private bool _disposed;
    internal static EmiTourNarrator? Active { get; private set; }

    internal static void Attach(TutorialService service)
    {
        if (Active is { _disposed: false } current && ReferenceEquals(current._service, service)) return;
        Detach();
        Active = new(service, EmiDeskService.Instance.SpeakTourMoment,
            id => EmiLineEngine.Instance.MomentIds.Contains(id, StringComparer.Ordinal));
    }

    internal static void Detach() { Active?.Dispose(); Active = null; }

    internal EmiTourNarrator(TutorialService service, Action<string> fire, Func<string, bool> hasMoment)
    {
        _service = service;
        _fire = fire;
        _hasMoment = hasMoment;
        service.TutorialStarted += Started;
        service.StepChanged += StepChanged;
        service.TutorialFinished += Finished;
    }

    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour narration failed"); }
    }

    private void Started(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _narrating = true;
        Safely(() => _fire("tourStarted"));
    }

    private void StepChanged(object? sender, TutorialStep step)
    {
        if (_disposed || !_narrating || string.IsNullOrWhiteSpace(step.Id)) return;
        Safely(() => _fire(_hasMoment("tour." + step.Id) ? "tour." + step.Id : "tourStep"));
    }

    private void Finished(object? sender, TutorialFinishedEventArgs e)
    {
        if (_disposed || !_narrating) return;
        _narrating = false;
        Safely(() => _fire(e.Completed ? "tourFinished" : "tourSkipped"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _narrating = false;
        _service.TutorialStarted -= Started;
        _service.StepChanged -= StepChanged;
        _service.TutorialFinished -= Finished;
    }
}
