// WPF Controls/Leash/LeashSurfaces.cs:80-98,171-182: report a receipt only after its notice is shown.
using System;
using System.Collections.Generic;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed class LeashEventNotices
{
    private readonly List<LeashEvent> _held = new();
    private readonly Action<LeashEvent> _show;
    private readonly Action<string?> _seen;

    internal LeashEventNotices(Action<LeashEvent> show, Action<string?> seen)
    {
        _show = show;
        _seen = seen;
    }

    internal void Arrived(LeashEvent e, bool panel, bool tugWobbles = false)
    {
        if (!LeashUiRules.CarriesReceipt(e.Kind)) return;
        var (toast, seen, hold) = LeashUiRules.EventNotice(e.Kind, panel, tugWobbles);
        if (toast && panel && LeashUiRules.EventKey(e) != null)
        {
            _show(e);
            if (seen) _seen(e.Id);
        }
        else if (seen && tugWobbles) _seen(e.Id);
        if (hold && !_held.Exists(item => item.Id == e.Id))
        {
            _held.Add(e);
            while (_held.Count > LeashUiRules.HeldNotices) _held.RemoveAt(0);
        }
    }

    internal void Flush(bool panel)
    {
        if (!panel) return;
        var pending = _held.ToArray();
        _held.Clear();
        foreach (var e in pending)
        {
            if (LeashUiRules.EventKey(e) == null) continue;
            try { _show(e); _seen(e.Id); }
            catch (Exception ex) { Serilog.Log.Debug("Leash held notice failed: {E}", ex.Message); }
        }
    }
}
