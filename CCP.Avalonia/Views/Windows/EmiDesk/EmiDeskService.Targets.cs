using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

internal sealed partial class EmiDeskService
{
    // WPF Services/EmiDesk/EmiDeskService.cs:1418,1450,1485.
    internal bool IsTargetAvailable(string? id)
    {
        var target = EmiTargets.Find(id);
        return target != null && target.Available && !target.Locked;
    }

    internal void OpenTarget(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OpenTarget(id)); return; }
        try { EmiTargets.Find(id)?.Open(); }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] target {Target} failed to open", id); }
    }

    internal void PinTop(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || EmiTargets.Find(id) == null) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => PinTop(id)); return; }
        try
        {
            EmiSuggester.PinToTop(id);
            _window?.RebuildRing();
            Fire("pinAdded", new { target = id });
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] target {Target} failed to pin", id); }
    }
}
