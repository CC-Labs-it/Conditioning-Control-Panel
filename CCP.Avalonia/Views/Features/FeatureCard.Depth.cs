using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Features;

public partial class FeatureCard
{
    private readonly TranslateTransform _travel = new();
    private readonly RotateTransform _tilt = new();
    private bool _pressed;
    private double _travelTarget = double.NaN;

    // WPF FeatureCard.xaml.cs:313-315 and Services/UI/DashboardCardDepth.cs:40-62.
    private void InitializeDepth()
    {
        this.FindControl<Panel>("PressFace")!.RenderTransform = new TransformGroup { Children = { _tilt, _travel } };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            var point = e.GetCurrentPoint(this);
            if (!DashboardDepth || IsLocked || (e.Source is Visual src && (src == _btnHelp || _btnHelp.IsVisualAncestorOf(src)))) return;
            if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed) return;
            _pressed = true;
            RefreshDepth();
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => ReleaseDepth(), RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => ReleaseDepth();
        PointerExited += (_, _) => ReleaseDepth();
        PointerMoved += (_, e) =>
        {
            var at = e.GetPosition(this);
            var size = Bounds.Size;
            if (_hovered && !_pressed && DashboardDepth && Env.Level != MotionLevel.Off
                && Env.CurrentTier != PerformanceTier.Performance && size.Width > 0 && size.Height > 0)
                SetDepthValue(_tilt, RotateTransform.AngleProperty,
                    DashboardDepthLaw.TiltFor(at.X / size.Width * 2 - 1, at.Y / size.Height * 2 - 1),
                    DepthLaw.Ms(DepthLaw.HoverMs, Env.Level), new LinearEasing());
        };
        Unloaded += (_, _) => { _pressed = false; ApplyHover(false); RefreshDepth(); };
        Loaded += (_, _) => RefreshDepth();
    }

    private void ReleaseDepth() { _pressed = false; RefreshDepth(); }

    // WPF FeatureCard.xaml.cs:564-608. Core owns the mute priority and transition duration.
    private void ApplyRestAppearance(bool snapMute = false)
    {
        bool active = IsActive || TutorialPreview;
        _contentRoot.Opacity = IsLocked ? 0.35
            : DimWhenInactive && !active && (DashboardDepth || !_hovered) ? 0.62 : 1;
        bool muted = CardMuteRule.ShouldMute(DimWhenInactive, active, IsLocked, _hovered && !DashboardDepth, TeaseTier > 0);
        _txtTitle.Opacity = muted ? 0.72 : 1;
        var grey = this.FindControl<Border>("ImgIconMute")!;
        int ms = snapMute ? 0 : CardMuteRule.TransitionMs(Env.AllowTransitions, IsLoaded);
        // WPF ApplyMute (:598-602) retargets from the current opacity. Replacing this
        // collection would first cancel the fade and expose its old destination.
        if (ms == 0) grey.Transitions = null;
        else if (grey.Transitions is not [DoubleTransition fade] || fade.Property != OpacityProperty
            || fade.Duration != TimeSpan.FromMilliseconds(ms) || fade.Easing is not QuadraticEaseOut)
            grey.Transitions = new Transitions
            { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut() } };
        grey.Opacity = muted ? 1 : 0;
        RefreshDepth();
    }

    // WPF Services/UI/DashboardCardDepth.cs:110-147. Same laws and spring as SplitFeatureCard.
    private void RefreshDepth()
    {
        var bevel = this.FindControl<Border>("DepthBevel")!;
        this.FindControl<Border>("DepthSocket")!.IsVisible = bevel.IsVisible = DashboardDepth;
        bool active = DashboardDepth && DimWhenInactive && IsActive && !IsLocked;
        bevel.Opacity = _pressed ? 1 : active ? 0.75 : 0.9;
        if (this.TryFindResource(DashboardDepthLaw.BevelKeyFor(_pressed, active), out var lamp) && lamp is IBrush brush)
            bevel.BorderBrush = brush;
        double target = DashboardDepthLaw.TravelFor(DashboardDepth, _pressed, active, _hovered);
        // WPF DashboardCardDepth.cs:122-124 preserves an unchanged tween on refresh.
        if (target != _travelTarget || !Env.AllowTransitions)
        {
            _travelTarget = target;
            double from = _travel.Y;
            SetDepthValue(_travel, TranslateTransform.YProperty, target,
                DepthLaw.Ms(_pressed ? DepthLaw.PressMs : DepthLaw.ReleaseMs, Env.Level),
                !_pressed && target < from ? new DepthSpring(from, target) : new QuadraticEaseOut());
        }
        if (!_hovered || _pressed || !DashboardDepth || Env.Level == MotionLevel.Off || Env.CurrentTier == PerformanceTier.Performance)
            SetDepthValue(_tilt, RotateTransform.AngleProperty, 0, DepthLaw.Ms(DepthLaw.HoverMs, Env.Level), new QuadraticEaseOut());
    }

    private static void Snap(Animatable target, Action set)
    {
        var transitions = target.Transitions;
        target.Transitions = null;
        set();
        target.Transitions = transitions;
    }

    private void SetDepthValue<T>(T transform, StyledProperty<double> property, double value, int ms, Easing easing)
        where T : Transform
    {
        if (ms <= 0 || !IsLoaded || !IsEffectivelyVisible)
        { Snap(transform, () => transform.SetValue(property, value)); return; }
        var duration = TimeSpan.FromMilliseconds(ms);
        // Reuse the transform's one transition when nothing about it changed; a new set cancels the tween.
        if (transform.Transitions is not [DoubleTransition t] || t.Property != property || t.Duration != duration
            || t.Easing.GetType() != easing.GetType() || easing is DepthSpring)
        {
            // WPF DashboardCardDepth.cs:124-141 starts at the animated position captured
            // before cancellation. Seed that base value before installing the new transition.
            double from = transform.GetValue(property);
            transform.Transitions = null;
            transform.SetValue(property, from);
            transform.Transitions = new Transitions
            {
                new DoubleTransition { Property = property, Duration = duration, Easing = easing }
            };
        }
        transform.SetValue(property, value);
    }

    // WPF's release keyframes: 65% reaches one pixel above rest, then settles.
    private sealed class DepthSpring(double from, double target) : Easing
    {
        public override double Ease(double progress)
        {
            double peak = 1 + DepthLaw.ReleaseOvershootPx / (from - target);
            if (progress <= 0.65)
            {
                double t = progress / 0.65;
                return peak * (1 - (1 - t) * (1 - t));
            }
            double u = (progress - 0.65) / 0.35;
            double ease = u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2;
            return peak + (1 - peak) * ease;
        }
    }
}
