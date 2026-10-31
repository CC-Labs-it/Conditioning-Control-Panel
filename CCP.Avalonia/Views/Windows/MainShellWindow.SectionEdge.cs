using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private Border[]? _edgeLifts;
    private BeatLoop? _edgeLap;
    private MotionLevel _edgeLevel;
    private double _edgeSeconds;

    private void EnsureEdgeLifts()
    {
        if (_edgeLifts != null || Named<Border>("SectionEdgeLiftTop") is not { Parent: Panel host } top) return;
        var lifts = new[] { top, new Border { Name = "SectionEdgeLiftRight" },
            new Border { Name = "SectionEdgeLiftBottom" }, new Border { Name = "SectionEdgeLiftLeft" } };
        for (int side = 0; side < 4; side++)
        {
            var lift = lifts[side];
            bool horizontal = side % 2 == 0;
            lift.IsHitTestVisible = false;
            if (horizontal)
            {
                lift.Height = 3;
                lift.Margin = new Thickness(8, 0);
                lift.VerticalAlignment = side == 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            }
            else
            {
                lift.Width = 3;
                lift.Margin = new Thickness(0, 8);
                lift.HorizontalAlignment = side == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            lift.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(horizontal ? 0 : 0.5, horizontal ? 0.5 : 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(horizontal ? 1 : 0.5, horizontal ? 0.5 : 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Colors.Transparent, 0.4), new GradientStop(Colors.Black, 0.5),
                    new GradientStop(Colors.Transparent, 0.6),
                },
            };
        }
        for (int side = 1; side < lifts.Length; side++) host.Children.Add(lifts[side]);
        _edgeLifts = lifts;
        PlaceEdgeLift(0);
        Activated += (_, _) => UpdateEdgeLap();
        Deactivated += (_, _) => UpdateEdgeLap();
        Closed += (_, _) => { _edgeLap?.Stop(); _edgeLap = null; };
    }

    private void UpdateEdgeLap()
    {
        if (_edgeLifts == null) return;
        _edgeLevel = SectionEdgeMotion.EffectiveLevel(AmbientFxCanvas.Env.Level, AmbientFxCanvas.Env.AllowAmbientLoops);
        bool run = SectionEdgeMotion.ShouldRun(_edgeLevel, IsActive, IsVisible, WindowState == WindowState.Minimized);
        if (!run) { _edgeLap?.Stop(); _edgeLap = null; }
        if (_edgeLevel != MotionLevel.Full) { _edgeSeconds = 0; PlaceEdgeLift(0); }
        else if (run && _edgeLap == null)
        {
            var offset = _edgeSeconds;
            PlaceEdgeLift(offset);
            _edgeLap = new BeatLoop(_edgeLifts[0], seconds => { _edgeSeconds = offset + seconds; PlaceEdgeLift(_edgeSeconds); }, TimeSpan.FromSeconds(1.0 / 24));
            _edgeLap.Start();
        }
    }

    internal void PlaceEdgeLift(double seconds)
    {
        if (_edgeLifts == null) return;
        for (int side = 0; side < 4; side++)
        {
            var lift = _edgeLifts[side];
            lift.IsVisible = _edgeLevel != MotionLevel.Off;
            var mask = (LinearGradientBrush)lift.OpacityMask!;
            var position = SectionEdgeMotion.Position(side, seconds, _edgeLevel);
            for (int stop = 0; stop < 3; stop++) mask.GradientStops[stop].Offset = 0.4 + stop * 0.1 + position;
        }
    }
}
