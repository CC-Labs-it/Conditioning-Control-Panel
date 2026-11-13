using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private double _entranceTime = -1;

    private void StartEntrance()
    {
        _entranceTime = Env.AllowTransitions ? 0 : -1;
        PaintEntrance();
        if (_entranceTime >= 0) EnsureFxClock();
    }

    private void SettleEntrance()
    {
        _entranceTime = -1;
        PaintEntrance();
    }

    private bool StepEntrance(double dt)
    {
        if (_entranceTime < 0) return false;
        if (!Env.AllowTransitions) { SettleEntrance(); return false; }
        _entranceTime += dt;
        PaintEntrance();
        if (_entranceTime >= 0.5) { SettleEntrance(); return false; }
        return true;
    }

    private void PaintEntrance()
    {
        int i = 0;
        foreach (var tile in GamesGrid.Children.OfType<Border>())
        {
            double elapsed = _entranceTime < 0 ? 1 : Math.Max(0, _entranceTime - Math.Min(i, 6) * 0.04);
            double fade = Math.Clamp(elapsed / 0.22, 0, 1);
            double rise = Math.Clamp(elapsed / 0.26, 0, 1);
            tile.Opacity = 1 - (1 - fade) * (1 - fade);
            if (tile.RenderTransform is TransformGroup group && group.Children.LastOrDefault() is TranslateTransform slide)
                slide.Y = 10 * (1 - rise) * (1 - rise);
            i++;
        }
    }

    private static void HookPress(Button button)
    {
        var scale = new ScaleTransform(1, 1);
        button.RenderTransformOrigin = RelativePoint.Center;
        button.RenderTransform = scale;
        void Set(bool down)
        {
            scale.Transitions = Env.AllowTransitions ? new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(InteractionPressMotion.DurationMs), Easing = new QuadraticEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(InteractionPressMotion.DurationMs), Easing = new QuadraticEaseOut() },
            } : null;
            scale.ScaleX = scale.ScaleY = InteractionPressMotion.ScaleFor(down);
        }
        button.AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) Set(true); }, RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, (_, e) => Set(false), RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => Set(false);
    }
}
