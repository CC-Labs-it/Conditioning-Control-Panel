// WPF LauncherWindow.Backdrop.cs:26-43,213-275,283-355; Tiles.cs:100-116; Fx.cs:411-439.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private sealed class DepthLayer
    {
        public readonly TranslateTransform Shift = new();
        public readonly double Pixels;
        public Tween X, Y;
        public DepthLayer(double pixels) => Pixels = pixels;
    }

    // WPF Backdrop.cs: near layers follow the cursor, spirals move against it.
    private readonly DepthLayer[] _depth = { new(18), new(14), new(8), new(-12), new(-8.4) };
    private DateTime _lastParallax = DateTime.MinValue;
    private sealed class ArtDrift
    {
        public Image Art = null!;
        public readonly ScaleTransform Scale = new(1, 1);
        public bool Outward;
        public double From = 1, To, T, Duration, ApplyIn;
    }
    private readonly List<ArtDrift> _artDrifts = new();
    private int _artSeed;
    private readonly ScaleTransform _wordmarkScale = new(1, 1);
    private double _wordmarkTime, _wordmarkApplyIn;

    private void HookDepth()
    {
        Wordmark.RenderTransformOrigin = RelativePoint.Center;
        Wordmark.RenderTransform = _wordmarkScale;
        GlowLayer.RenderTransform = _depth[0].Shift;
        PoolLayer.RenderTransform = _depth[1].Shift;
        Ambient.RenderTransform = _depth[2].Shift;
        AddHandler(PointerMovedEvent, (_, e) => MoveDepth(e.GetPosition(this)), handledEventsToo: true);
        PointerExited += (_, _) => TargetDepth(0, 0);
    }

    internal void MoveDepth(Point at)
    {
        if (!_fxActive || !FxLive || !TiltAllowed || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var now = LauncherSfx.Clock.GetUtcNow().UtcDateTime;
        if ((now - _lastParallax).TotalMilliseconds < 33) return;
        _lastParallax = now;
        TargetDepth(Math.Clamp(at.X / Bounds.Width * 2 - 1, -1, 1),
            Math.Clamp(at.Y / Bounds.Height * 2 - 1, -1, 1));
    }

    private void TargetDepth(double nx, double ny)
    {
        foreach (var d in _depth)
        {
            double seconds = TiltAllowed && FxLive ? 0.26 : 0;
            d.X.Go(d.Shift.X, nx * d.Pixels, seconds);
            d.Y.Go(d.Shift.Y, ny * d.Pixels, seconds);
            if (seconds == 0) { d.Shift.X = d.X.To; d.Shift.Y = d.Y.To; }
        }
        EnsureFxClock();
    }

    private void ParkDepth()
    {
        _wordmarkTime = 0;
        _wordmarkScale.ScaleX = _wordmarkScale.ScaleY = 1;
        foreach (var d in _depth)
        {
            d.X.On = d.Y.On = false;
            d.Shift.X = d.Shift.Y = 0;
        }
    }

    internal void DecorateArt(Image art)
    {
        _artDrifts.RemoveAll(d => d.Art.Parent == null || !GamesGrid.IsVisualAncestorOf(d.Art));
        var d = new ArtDrift { Art = art, Outward = (_artSeed++ & 1) == 0 };
        art.RenderTransformOrigin = RelativePoint.Center;
        art.RenderTransform = d.Scale;
        NextArtLeg(d);
        _artDrifts.Add(d);
        // Register before HoverPop: the drift pauses before its rig wraps our scale.
        art.PointerExited += (_, _) => NextArtLeg(d);
        HoverPop.SetIsEnabled(art, true);
    }

    private static void NextArtLeg(ArtDrift d)
    {
        d.From = d.Scale.ScaleX;
        d.To = d.Outward ? 1.07 : 1;
        if (Math.Abs(d.From - d.To) < 0.002) d.To = d.Outward ? 1 : 1.07;
        d.Duration = 14 * Math.Abs(d.To - d.From) / 0.07;
        d.T = d.ApplyIn = 0;
    }

    private bool StepDepth(double dt)
    {
        bool busy = false;
        foreach (var d in _depth)
        {
            if (d.X.On) { d.Shift.X = d.X.Step(dt); busy = true; }
            if (d.Y.On) { d.Shift.Y = d.Y.Step(dt); busy = true; }
        }
        if (!_fxActive || !Env.AllowAmbientLoops) return busy;
        _wordmarkTime += dt;
        if ((_wordmarkApplyIn -= dt) <= 0)
        {
            _wordmarkApplyIn = 0.1;
            double u = _wordmarkTime % 80 / 40;
            if (u > 1) u = 2 - u;
            _wordmarkScale.ScaleX = _wordmarkScale.ScaleY = 1 + 0.04 * (1 - Math.Cos(Math.PI * u)) / 2;
        }
        busy = true;
        if (PerfLow) return busy;
        foreach (var d in _artDrifts)
        {
            if (d.Art.IsPointerOver) continue;
            busy = true;
            d.T += dt;
            if ((d.ApplyIn -= dt) > 0) continue;
            d.ApplyIn = 1.0 / 12;
            double u = Math.Min(1, d.T / d.Duration);
            d.Scale.ScaleX = d.Scale.ScaleY = d.From + (d.To - d.From) * (1 - Math.Cos(Math.PI * u)) / 2;
            if (u >= 1) { d.Outward = d.To < 1.035; NextArtLeg(d); }
        }
        return busy;
    }
}
