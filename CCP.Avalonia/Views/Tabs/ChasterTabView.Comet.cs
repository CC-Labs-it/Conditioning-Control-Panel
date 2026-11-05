using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private PerimeterCometAdorner? _linkComet;
    private bool _linking;

    // WPF ChasterTabView.Fx :756: the wait laps the hero at six seconds, ambient-only.
    private void RefreshLinkComet()
    {
        StopLinkComet();
        if (_linking && IsEffectivelyVisible && AmbientFxCanvas.Env.AllowAmbientLoops)
            _linkComet = PerimeterCometAdorner.Attach(HeroCard, HeroCard.CornerRadius.TopLeft, 6);
    }

    private void StopLinkComet()
    {
        PerimeterCometAdorner.Detach(_linkComet);
        _linkComet = null;
    }

    private void HookLinkComet()
    {
        AttachedToVisualTree += (_, _) => AmbientFxCanvas.Env.MotionGateChanged += RefreshLinkComet;
        DetachedFromVisualTree += (_, _) => AmbientFxCanvas.Env.MotionGateChanged -= RefreshLinkComet;
        // Visibility propagation and the layer can finish after OnTabShown/Loaded.
        Loaded += (_, _) => Dispatcher.UIThread.Post(RefreshLinkComet, DispatcherPriority.Loaded);
        DetachedFromVisualTree += (_, _) => StopLinkComet();
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (!IsVisible) StopLinkComet();
            else Dispatcher.UIThread.Post(RefreshLinkComet, DispatcherPriority.Loaded);
        };
    }
}
