// WPF ChasterTabView.Fx.cs:362-379: paper tag swings on its string.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private BeatLoop? _tagSwing;
    private IDisposable? _tagVisibility;
    private readonly RotateTransform _tagRotation = new(-5);
    internal bool TagSwingRunning => _tagSwing?.IsTicking == true;
    internal double TagSwingAngle => _tagRotation.Angle;

    private void InitializeTagSwing()
    {
        PaperTag.RenderTransform = _tagRotation;
        PropertyChanged += TagVisibilityChanged;
        PaperTag.PropertyChanged += TagVisibilityChanged;
        AttachedToVisualTree += (_, _) =>
        {
            _tagVisibility = EffectiveVisibility.Watch(PaperTag, () => Dispatcher.UIThread.Post(RefreshTagSwing));
            AmbientFxCanvas.Env.MotionGateChanged += RefreshTagSwing;
            RefreshTagSwing();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _tagVisibility?.Dispose();
            _tagVisibility = null;
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshTagSwing;
            StopTagSwing();
        };
    }

    private void TagVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty) return;
        if (e.NewValue is false) StopTagSwing();
        else Dispatcher.UIThread.Post(RefreshTagSwing);
    }

    private void RefreshTagSwing()
    {
        if (VisualRoot == null || PaperTag.GetSelfAndVisualAncestors().Any(v => !v.IsVisible) || !AmbientFxCanvas.Env.AllowAmbientLoops)
        {
            StopTagSwing();
            return;
        }
        if (_tagSwing != null) return;
        _tagSwing = new BeatLoop(PaperTag, t => _tagRotation.Angle = -7 + 4 * BeatLoop.Breath(t, 3.6));
        _tagSwing.Start();
    }

    private void StopTagSwing()
    {
        _tagSwing?.Stop();
        _tagSwing = null;
        _tagRotation.Angle = -5;
    }
}
