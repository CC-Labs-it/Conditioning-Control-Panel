using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private PerimeterCometAdorner? _hoverComet, _panelComet;

    // WPF LauncherWindow.Fx :134 and Choreo :395: ambient-only, never on the Performance tier.
    private bool CometsAllowed => FxLive && _fxActive && !PerfLow && AmbientFxCanvas.Env.AllowAmbientLoops;

    private void HoverComet(Border tile, bool on)
    {
        ClearHoverComet();
        if (on && CometsAllowed)
            _hoverComet = PerimeterCometAdorner.Attach(tile, TileRadius, 7);
    }

    private void ClearHoverComet()
    {
        PerimeterCometAdorner.Detach(_hoverComet);
        _hoverComet = null;
    }

    private void RefreshPanelComet()
    {
        bool live = CoreEngine.IsRunning && CometsAllowed;
        if (live == (_panelComet != null)) return;
        if (live)
            _panelComet = PerimeterCometAdorner.Attach(PanelCard, PanelCard.CornerRadius.TopLeft, 12);
        else
        {
            PerimeterCometAdorner.Detach(_panelComet);
            _panelComet = null;
        }
    }

    private void ParkComets()
    {
        ClearHoverComet();
        PerimeterCometAdorner.Detach(_panelComet);
        _panelComet = null;
    }

    // Status can arrive before the adorner layer; Loaded is the WPF attach-on-arrival seam.
    private void QueuePanelComet() => Dispatcher.UIThread.Post(RefreshPanelComet, DispatcherPriority.Loaded);
}
