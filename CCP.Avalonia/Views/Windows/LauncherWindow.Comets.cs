using System;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private PerimeterCometAdorner? _hoverComet, _panelComet;
    private SolidColorBrush? _panelRim;      // the breathing copy of the rim, while the panel comet runs
    private IDisposable? _panelRimHold;      // above the XAML DynamicResource; disposing it restores the rest rim
    private double _panelRimT;
    private const double PanelRimBreathSeconds = 2.6;   // WPF Choreo.cs:47

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
        {
            _panelComet = PerimeterCometAdorner.Attach(PanelCard, PanelCard.CornerRadius.TopLeft, 12);
            // WPF Choreo.cs:403-416: the rim breathes 0.45 <-> 1 (sine, auto-reverse) on the frame clock (P82).
            if (_panelComet != null && PanelCard.BorderBrush is ISolidColorBrush rim)
            {
                _panelRim = new SolidColorBrush(rim.Color, 0.45);
                _panelRimT = 0;
                _panelRimHold = PanelCard.SetValue(Border.BorderBrushProperty, _panelRim, BindingPriority.Animation);
                EnsureFxClock();
            }
        }
        else ParkPanelComet();
    }

    private void ParkPanelComet()
    {
        PerimeterCometAdorner.Detach(_panelComet);
        _panelComet = null;
        _panelRimHold?.Dispose();
        _panelRimHold = null;
        _panelRim = null;
    }

    /// <summary>One frame of the rim breath; false when no panel comet runs.</summary>
    private bool StepPanelRim(double dt)
    {
        if (_panelRim == null) return false;
        _panelRimT = (_panelRimT + dt) % (2 * PanelRimBreathSeconds);
        double u = _panelRimT / PanelRimBreathSeconds;
        if (u > 1) u = 2 - u;
        _panelRim.Opacity = 0.45 + 0.55 * (1 - Math.Cos(Math.PI * u)) / 2;
        return true;
    }

    internal double? PanelRimOpacity => _panelRim?.Opacity;

    private void ParkComets()
    {
        ClearHoverComet();
        ParkPanelComet();
    }

    // Status can arrive before the adorner layer; Loaded is the WPF attach-on-arrival seam.
    private void QueuePanelComet() => Dispatcher.UIThread.Post(RefreshPanelComet, DispatcherPriority.Loaded);
}
