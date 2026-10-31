using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Tours;

/// <summary>
/// The summon half of WPF Services/EmiDesk/EmiTourNarrator.cs:119-145.
/// Speech moments and the book offer still need the desk speech bus on this head.
/// </summary>
internal sealed class EmiTourPresence : IDisposable
{
    private readonly TutorialService _service;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _isOut;
    private readonly Func<Task> _summon;
    private bool _disposed;
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
            () => EmiDeskService.Instance.Summon("tour"));
    }

    internal static void Detach()
    {
        Active?.Dispose();
        Active = null;
    }

    internal EmiTourPresence(TutorialService service, Func<bool> enabled, Func<bool> isOut, Func<Task> summon)
    {
        _service = service;
        _enabled = enabled;
        _isOut = isOut;
        _summon = summon;
        _service.TutorialStarted += OnStarted;
    }

    private void OnStarted(object? sender, EventArgs e)
    {
        if (!_disposed) PendingSummon = SummonIfAway();
    }

    private async Task SummonIfAway()
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
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.TutorialStarted -= OnStarted;
    }
}
