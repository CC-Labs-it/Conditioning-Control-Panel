using System;
using System.Collections.Generic;
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
    private readonly Dictionary<Border, CancellationTokenSource> _bumps = new();
    private Window? _faceHost;
    internal int ActiveBumps => _bumps.Count;

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
    // WPF FriendsRailChip.cs:363-373: 120 ms rise, 300 ms elastic settle.
    private void Bump(Border target)
    {
        if (_bumps.Remove(target, out var old)) { old.Cancel(); old.Dispose(); }
        var amount = FriendsDrawerRules.MotionAmount(CoreSettings.Current.MotionLevel);
        if (amount <= 0) return;
        var run = new CancellationTokenSource();
        _bumps[target] = run;
        _ = PlayBump(target, amount, run);
    }
    private async Task PlayBump(Border target, double amount, CancellationTokenSource run)
    {
        try
        {
            var peak = 1 + .45 * amount;
            await ScalePhase(target, 1, peak, 120, new LinearEasing(), run.Token);
            await ScalePhase(target, peak, 1, 300, new WpfElasticOut(), run.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_bumps.TryGetValue(target, out var current) && ReferenceEquals(current, run)) _bumps.Remove(target);
            run.Dispose();
        }
    }
    private static Task ScalePhase(Border target, double from, double to, int milliseconds, Easing easing, CancellationToken token)
    {
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = easing, Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleXProperty, from), new Setter(ScaleTransform.ScaleYProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleXProperty, to), new Setter(ScaleTransform.ScaleYProperty, to) } },
        } };
        return animation.RunAsync(target, token);
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
        foreach (var run in _bumps.Values) run.Cancel();
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
    private void OnFaceActivated(object? sender, EventArgs e) => RefreshFace();

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
