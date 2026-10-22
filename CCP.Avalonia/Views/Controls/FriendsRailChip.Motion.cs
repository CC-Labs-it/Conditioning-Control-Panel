using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsRailChip
{
    private readonly Dictionary<Border, CancellationTokenSource> _bumps = new();
    private Window? _host;
    internal int ActiveBumps => _bumps.Count;

    private void WireInput()
    {
        PointerEntered += (_, _) => Hover(true);
        PointerExited += (_, _) => Hover(false);
        Drawer.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_host is not { IsActive: false } host) return;
            var box = e.Source as Visual;
            while (box != null && box is not TextBox) box = box.GetVisualParent();
            host.Activate();
            var focus = box as InputElement ?? Drawer;
            Dispatcher.UIThread.Post(() => focus.Focus(), DispatcherPriority.Input);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
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
            await ScalePhase(target, peak, 1, 300, new ElasticEaseOut(), run.Token);
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
    private void CancelBumps()
    {
        foreach (var run in _bumps.Values) run.Cancel();
        _bumps.Clear();
    }
    private void BindHost()
    {
        UnbindHost();
        _host = TopLevel.GetTopLevel(this) as Window;
        if (_host == null) return;
        _host.Activated += OnActivated;
        _host.Deactivated += OnDeactivated;
        _host.PositionChanged += OnHostMoved;
        _host.PropertyChanged += OnHostProperty;
    }
    private void UnbindHost()
    {
        if (_host == null) return;
        _host.Activated -= OnActivated; _host.Deactivated -= OnDeactivated;
        _host.PositionChanged -= OnHostMoved; _host.PropertyChanged -= OnHostProperty;
        _host = null;
    }
    private void OnActivated(object? sender, EventArgs e) { Rebind(); RefreshFace(); if (IsOpen) DropTopmost(); }
    private void OnDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => { if (_host?.IsActive == false && !Drawer.IsKeyboardFocusWithin) _popup.IsOpen = false; });
    }
    private void OnHostMoved(object? sender, PixelPointEventArgs e) => _popup.IsOpen = false;
    private void OnHostProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    { if (e.Property == Window.WindowStateProperty) _popup.IsOpen = false; }
    private void DropTopmost()
    {
        if (TopLevel.GetTopLevel(Drawer) is WindowBase popupRoot) popupRoot.Topmost = false;
    }
}
