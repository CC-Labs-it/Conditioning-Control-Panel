// WPF ChasterTabView.Fx.cs:87-98, MotionFx.PressSquish: the link and consent buttons.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private readonly Dictionary<Button, BeatLoop> _pressBeats = new();

    private void InitPressFeedback()
    {
        foreach (var button in new[] { BtnLink, BtnConsentOk })
        {
            button.RenderTransformOrigin = RelativePoint.Center;
            button.RenderTransform = new ScaleTransform(1, 1);
            button.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) PressFeedback(button, true);
            }, RoutingStrategies.Tunnel);
            button.AddHandler(PointerReleasedEvent, (_, _) => PressFeedback(button, false), RoutingStrategies.Tunnel);
            button.PointerExited += (_, _) => PressFeedback(button, false);
            button.PointerCaptureLost += (_, _) => PressFeedback(button, false);
        }
        DetachedFromVisualTree += (_, _) => StopPressFeedback();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) StopPressFeedback(); };
        AttachedToVisualTree += (_, _) => AmbientFxCanvas.Env.MotionGateChanged += PressMotionChanged;
        DetachedFromVisualTree += (_, _) => AmbientFxCanvas.Env.MotionGateChanged -= PressMotionChanged;
    }

    private void PressMotionChanged() { if (!AmbientFxCanvas.Env.AllowTransitions) StopPressFeedback(); }

    private void PressFeedback(Button button, bool down)
    {
        if (_pressBeats.Remove(button, out var old)) old.Stop();
        if (button.RenderTransform is not ScaleTransform scale) return;
        double from = scale.ScaleX, target = InteractionPressMotion.ScaleFor(down);
        if (!AmbientFxCanvas.Env.AllowTransitions || !button.IsEffectivelyVisible)
        {
            scale.ScaleX = scale.ScaleY = target;
            return;
        }
        BeatLoop? beat = null;
        beat = new BeatLoop(button, seconds =>
        {
            var t = Math.Clamp(seconds * 1000 / InteractionPressMotion.DurationMs, 0, 1);
            var eased = 1 - (1 - t) * (1 - t);
            scale.ScaleX = scale.ScaleY = from + (target - from) * eased;
            if (t >= 1 && beat != null) { beat.Stop(); _pressBeats.Remove(button); }
        }) { OnParked = () => ResetPress(button) };
        _pressBeats[button] = beat;
        beat.Start();
    }

    private void ResetPress(Button button)
    {
        if (_pressBeats.Remove(button, out var beat)) beat.Stop();
        if (button.RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = 1;
    }

    internal void StopPressFeedback()
    {
        ResetPress(BtnLink);
        ResetPress(BtnConsentOk);
    }
}
