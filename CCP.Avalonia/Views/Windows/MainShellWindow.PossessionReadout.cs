using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.Lab.cs:884-984. A generation also rejects queued work from an old run.
    private void InitializePossessionReadout(LockdownService lockdown, PossessionDirector director)
    {
        var tab = Named<LockdownTabView>("LockdownTab");
        if (tab == null) return;
        Action<PossessionRung>? handler = null;
        var generation = 0;
        var closed = false;
        void Clear()
        {
            generation++;
            if (handler != null) director.RungChanged -= handler;
            handler = null;
            tab.PaintPossessionReadout(null);
        }
        void Activate()
        {
            Clear();
            if (closed || !lockdown.IsActive || !CoreSettings.Current.LockdownPossessionEnabled) return;
            tab.PaintPossessionReadout(PossessionRung.Settle);
            var current = generation;
            handler = rung => Dispatcher.UIThread.Post(() =>
            {
                if (current == generation && lockdown.IsActive) tab.PaintPossessionReadout(rung);
            });
            director.RungChanged += handler;
        }
        void Activated() => Dispatcher.UIThread.Post(Activate);
        void Deactivated() => Dispatcher.UIThread.Post(Clear);
        lockdown.LockdownActivated += Activated;
        lockdown.LockdownDeactivated += Deactivated;
        Closed += (_, _) =>
        {
            closed = true;
            lockdown.LockdownActivated -= Activated;
            lockdown.LockdownDeactivated -= Deactivated;
            Clear();
        };
        if (lockdown.IsActive) Activate(); else Clear();
    }
}
