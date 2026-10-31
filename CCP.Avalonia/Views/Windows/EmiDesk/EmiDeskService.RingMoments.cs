using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

internal sealed record EmiMoment(string Id, object? Context);

// Re-cut from wire-win-emi-ring-b. WPF EmiDeskService.cs:661-705, Bubble.cs:503-536.
// Ask probes remain unset: offers need their own UI and effect runner before being enabled.
internal sealed partial class EmiDeskService
{
    internal EmiLineEngine LineEngine { get; set; } = EmiLineEngine.Instance;
    public event EventHandler<EmiMoment>? MomentFired;

    public void Fire(string id, object? context = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        try { MomentFired?.Invoke(this, new EmiMoment(id, context)); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] moment observer failed"); }
        if (!IsOut && !LineEngine.IsHoldMoment(id)) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => DrawRingMoment(id, context));
            return;
        }
        DrawRingMoment(id, context);
    }

    private void DrawRingMoment(string id, object? context)
    {
        try
        {
            // Re-check after dispatch: a queued moment must not spend a line after dismissal.
            if (!IsOut && !LineEngine.IsHoldMoment(id)) return;
            if (_window?.PresentationActive == true && IsOut) return;
            var line = LineEngine.Draw(id, EmiLineEngine.ToCtx(context));
            if (line != null && IsOut) _window?.SpeakRingLine(line, LineEngine);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring moment {Moment} failed", id); }
    }

    public void StopRingSpeechForPanic()
    {
        _window?.CancelChain();
        Fire("panicPressed");
    }
}
