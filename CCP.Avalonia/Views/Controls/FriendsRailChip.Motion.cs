using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Controls.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsRailChip
{
    private readonly Dictionary<Border, ConditioningControlPanel.Avalonia.Helpers.TransformTween> _bumps = new();
    private Window? _faceHost;
    internal int ActiveBumps => _bumps.Values.Count(t => t.IsRunning);

    internal void Hover(bool on)
    {
        _face.Background = on || IsOpen ? FriendsDrawer.Raised : Brushes.Transparent;
        // WPF HoverLift snaps at Off, and animates the same endpoint otherwise.
        if (_face.RenderTransform is not ScaleTransform scale) return;
        scale.Transitions = CoreSettings.Current.MotionLevel == ConditioningControlPanel.Models.MotionLevel.Off ? null : new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new QuadraticEaseOut() },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new QuadraticEaseOut() },
        };
        scale.ScaleX = scale.ScaleY = on ? 1.02 : 1;
    }
    // WPF FriendsRailChip.cs:363-373: 120 ms rise, 300 ms elastic settle, on ONE clock (no reset between phases).
    // Keyframes on the ScaleTransform itself go through Helpers/TransformTween: Animation.RunAsync on the Border
    // with ScaleTransform setters never moved the transform (Nov 4 review, #3657).
    private void Bump(Border target)
    {
        if (_bumps.Remove(target, out var old)) old.Stop();
        var amount = FriendsDrawerRules.MotionAmount(global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level);
        if (amount <= 0) return;
        target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        if (target.RenderTransform is not ScaleTransform scale) target.RenderTransform = scale = new ScaleTransform(1, 1);
        double peak = 1 + .45 * amount;
        _bumps[target] = ConditioningControlPanel.Avalonia.Helpers.TransformTween.Run(scale, TimeSpan.FromMilliseconds(420),
            new (double, AvaloniaProperty, double)[]
            {
                (0, ScaleTransform.ScaleXProperty, 1d), (1, ScaleTransform.ScaleXProperty, peak),
                (0, ScaleTransform.ScaleYProperty, 1d), (1, ScaleTransform.ScaleYProperty, peak),
            },
            new BumpTimeline());
    }
    private sealed class BumpTimeline : Easing
    {
        private readonly WpfElasticOut _settle = new();
        public override double Ease(double progress) => progress < 120d / 420
            ? progress * 420 / 120
            : 1 - _settle.Ease((progress * 420 - 120) / 300);
    }
    /// <summary>WPF ElasticEase { EasingMode = EaseOut, Oscillations = 1, Springiness = 4 }; Avalonia's ElasticEaseOut rings several times.</summary>
    internal sealed class WpfElasticOut : Easing
    {
        public override double Ease(double progress)
        {
            var t = 1 - progress;
            var spring = (Math.Exp(4 * t) - 1) / (Math.Exp(4) - 1);
            return 1 - spring * Math.Sin((Math.PI * 2 * 1 + Math.PI / 2) * t);
        }
    }
    private void CancelBumps()
    {
        foreach (var (target, tween) in _bumps)
        {
            tween.Stop();
            if (target.RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = 1;
        }
        _bumps.Clear();
    }
    // WPF FriendsRailChip.cs:197-228: refresh identity on attach and activation.
    private void BindFaceHost()
    {
        UnbindFaceHost();
        _faceHost = TopLevel.GetTopLevel(this) as Window;
        if (_faceHost != null) _faceHost.Activated += OnFaceActivated;
    }
    private void UnbindFaceHost()
    {
        if (_faceHost != null) _faceHost.Activated -= OnFaceActivated;
        _faceHost = null;
    }
    private void OnFaceActivated(object? sender, EventArgs e) { Rebind(); RefreshFace(); }

    // WPF FriendsRailChip.cs:279-296, FriendsLook.cs:260-275.
    internal void RefreshFace()
    {
        _name.Text = Drawer.MeName();
        if (_face.Child is Grid face) face.Children[0] = FriendsDrawer.Avatar(_name.Text!, 40, null);
        while (_nameLine.Children.Count > 1) _nameLine.Children.RemoveAt(1);
        var tier = FriendsDrawerRules.DisplayTier(Drawer.MeTier());
        if (tier > 0 && Helpers.ModArt.TryLoad($"features/tier_badge_t{tier}.png", 96) is { } art)
        {
            var plate = new Image { Source = art, Height = 13, Stretch = Stretch.Uniform, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Tag = $"friends-tier-{tier}" };
            Grid.SetColumn(plate, 1); _nameLine.Children.Add(plate);
        }
        UpdatePill(false);
    }
}
