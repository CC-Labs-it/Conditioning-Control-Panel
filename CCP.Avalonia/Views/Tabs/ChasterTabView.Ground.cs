// WPF ChasterTabView.Fx.cs:129, 252, 276, 296 and 340.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private IDisposable? _groundVisibility;
    private BeatLoop? _groundBeat;
    private bool _groundStarted;
    private double _groundAngle;
    private RotateTransform GroundTurn => (RotateTransform)SpiralLayer.RenderTransform!;
    private ScaleTransform ArtScale => (ScaleTransform)HeroArt.RenderTransform!;

    private void GroundLoaded(object? sender, RoutedEventArgs e)
    {
        if (_groundVisibility != null) return;
        if (SpiralPath.Data == null)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                const double center = 380, inner = 8;
                double max = 6.5 * Math.PI * 2, growth = (center - inner - 4) / max;
                ctx.BeginFigure(new Point(center + inner, center), false);
                for (double theta = Math.PI / 45; theta <= max; theta += Math.PI / 45)
                {
                    double r = inner + growth * theta;
                    ctx.LineTo(new Point(center + r * Math.Cos(theta), center + r * Math.Sin(theta)));
                }
                ctx.EndFigure(false);
            }
            SpiralPath.Data = geometry;
        }
        _groundVisibility = EffectiveVisibility.Watch(this, RefreshGround);
        AmbientFxCanvas.Env.MotionGateChanged += RefreshGround;
        Ground.Unloaded += GroundUnloaded;
        RefreshGround();
    }

    private void GroundUnloaded(object? sender, RoutedEventArgs e)
    {
        Ground.Unloaded -= GroundUnloaded;
        _groundVisibility?.Dispose();
        _groundVisibility = null;
        AmbientFxCanvas.Env.MotionGateChanged -= RefreshGround;
        ParkGround();
    }

    private void RefreshGround()
    {
        if (!IsEffectivelyVisible || VisualRoot == null) { ParkGround(); return; }
        if (!_groundStarted)
        {
            _groundStarted = true;
            GroundAmbient.StartLayers(new AmbientFxConfig { Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField, Intensity = 0.85, FogPuffs = 3 });
        }
        else GroundAmbient.Resume();
        (TopLevel.GetTopLevel(this) as MainShellWindow)?.RegisterTabFx("chaster", GroundAmbient);
        if (!AmbientFxCanvas.Env.AllowAmbientLoops || AmbientFxCanvas.Env.Level != MotionLevel.Full)
        {
            StopGroundMotion();
            return;
        }
        if (_groundBeat?.IsRunning == true) return;
        _groundAngle = GroundTurn.Angle;
        _groundBeat ??= new BeatLoop(Ground, t =>
        {
            GroundTurn.Angle = _groundAngle + 360 * t / 90;
            ArtScale.ScaleX = ArtScale.ScaleY = 1 + 0.06 * BeatLoop.Breath(t, 38);
        }, TimeSpan.FromMilliseconds(100));
        _groundBeat.Start();
    }

    private void StopGroundMotion()
    {
        _groundBeat?.Stop();
        ArtScale.ScaleX = ArtScale.ScaleY = 1;
    }

    private void ParkGround()
    {
        GroundAmbient.Pause();
        StopGroundMotion();
    }
}
