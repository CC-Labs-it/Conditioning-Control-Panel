using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF BouncingTextService.cs:133-170,390-416,564-750 and ScrawlScene.cs.
internal static partial class BouncingTextOverlay
{
    internal static Func<bool> ScrawlOn = () => SuperAccess.IsOn(SuperEffect.Scrawl);
    internal static Func<Action<int, int>, IDisposable?> ScrawlListen = GazeDriftMouseHook.Start;
    internal static bool ScrawlListenWhenTesting;
    private static ScrawlScene? _scrawl;
    private static IDisposable? _scrawlListener;
    internal static ScrawlScene? Scrawl => _scrawl;
    private static readonly Random ScrawlRandom = new();

    private static void StartScrawl()
    {
        SuperAccess.Changed -= ScrawlChanged;
        SuperAccess.Changed += ScrawlChanged;
        SyncScrawl();
    }

    private static void ScrawlChanged(SuperEffect effect)
    {
        if (effect != SuperEffect.Scrawl) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ScrawlChanged(effect)); return; }
        SyncScrawl();
    }

    private static void SyncScrawl()
    {
        if (!_running) return;
        if (!ScrawlOn()) { DisableScrawl(); return; }
        if (_scrawl != null) return;
        _scrawl = new ScrawlScene(Engine.FontSize, Family());
        foreach (var window in Windows) window.AttachScrawl(_scrawl);
        var scene = _scrawl;
        if (!SkipPlatformChecksForTests || ScrawlListenWhenTesting)
            _scrawlListener = ScrawlListen((x, y) => Dispatcher.UIThread.Post(() =>
            {
                if (_running && ReferenceEquals(scene, _scrawl)) ScrawlClick(x, y);
            }));
    }

    private static void StopScrawl()
    {
        SuperAccess.Changed -= ScrawlChanged;
        DisableScrawl();
    }

    private static void DisableScrawl()
    {
        var listener = _scrawlListener; _scrawlListener = null;
        if (listener != null) ThreadPool.QueueUserWorkItem(_ => { try { listener.Dispose(); } catch { } });
        if (_scrawl == null) return;
        _scrawl.Clear(); _scrawl = null;
        foreach (var w in Windows) w.DetachScrawl();
        foreach (var l in Engine.Logos)
        {
            l.SlamT = -1; l.SlamLanded = false;
            double speed = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
            if (speed > l.Cruise && speed > 0) { l.VelX *= l.Cruise / speed; l.VelY *= l.Cruise / speed; }
        }
    }

    private static void ResetScrawlForFont()
    {
        if (_scrawl == null || (_scrawl.FontSize == Engine.FontSize && _scrawl.Font == Engine.Font)) return;
        DisableScrawl(); SyncScrawl();
    }

    private static MotionLevel ScrawlMotion => AmbientFxCanvas.Env.Level;
    private static void TickScrawl(double dt)
    {
        if (_scrawl == null) return;
        var s = CoreSettings.Current;
        _scrawl.Tick(dt, ScrawlOptions.ClampStampSeconds(s.ScrawlStampSeconds), ScrawlOptions.ClampStampCap(s.ScrawlStampCap));
        foreach (var w in Windows) w.PaintScrawl();
    }

    private static void RelaxScrawl(BouncingTextEngine.Logo l, double dt)
    {
        if (_scrawl == null) return;
        var speed = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
        var factor = ScrawlRules.RelaxFactor(speed, l.Cruise, dt);
        l.VelX *= factor; l.VelY *= factor;
    }

    private static void ScrawlBounce(BouncingTextEngine.Logo l, BounceStep step)
    {
        if (_scrawl == null || !step.Bounced) return;
        var wall = ScrawlRules.WallOf(step.BouncedX && l.VelX > 0, step.BouncedX && l.VelX < 0,
            step.BouncedY && l.VelY > 0, step.BouncedY && l.VelY < 0);
        _scrawl.WallHit(l.Text, wall, l.PosX, l.PosY, l.TextWidth, l.TextHeight,
            new ScrawlBounds(Engine.MinX, Engine.MinY, Engine.MaxX, Engine.MaxY));
    }

    // The hook observes, never consumes, a click. Keep CCP controls usable even where a word overlaps them.
    internal static void ScrawlClick(int px, int py)
    {
        if (!_running || _scrawl == null || !ScrawlOptions.SlamOn(CoreSettings.Current.ScrawlSlam) || ClickOverOwnWindow(px, py)) return;
        double x = px / _screenScale, y = py / _screenScale;
        for (int i = Engine.Logos.Count - 1; i >= 0; i--)
        {
            var l = Engine.Logos[i];
            if (x < l.PosX - 8 || x > l.PosX + l.TextWidth + 8 || y < l.PosY - 8 || y > l.PosY + l.TextHeight + 8 || l.SlamT >= 0) continue;
            var screens = _host == null ? null : ScreenList.Enumerate(_host);
            var b = screens?.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint((int)((l.PosX + l.TextWidth / 2) * _screenScale), (int)((l.PosY + l.TextHeight / 2) * _screenScale))))?.Bounds;
            l.SlamX = b.HasValue ? (b.Value.X + b.Value.Width / 2.0) / _screenScale : (Engine.MinX + Engine.MaxX) / 2;
            l.SlamY = b.HasValue ? (b.Value.Y + b.Value.Height / 2.0) / _screenScale : (Engine.MinY + Engine.MaxY) / 2;
            l.SlamW = b.HasValue ? b.Value.Width / _screenScale : Engine.MaxX - Engine.MinX;
            if (ScrawlMotion == MotionLevel.Off) _scrawl.SlamLanded(l.Text, l.SlamX, l.SlamY);
            else { l.SlamT = 0; l.SlamLanded = false; }
            return;
        }
    }

    internal static Func<int, int, bool> ClickOverOwnWindow = (x, y) =>
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return false;
        return desktop.Windows.Any(w => w is not BouncingTextOverlayWindow && w.IsVisible &&
            x >= w.Position.X && y >= w.Position.Y && x < w.Position.X + w.Bounds.Width * w.DesktopScaling && y < w.Position.Y + w.Bounds.Height * w.DesktopScaling);
    };

    private static bool StepScrawlSlam(BouncingTextEngine.Logo l, double dt)
    {
        if (_scrawl == null || l.SlamT < 0) return false;
        l.SlamT += dt;
        double x = l.SlamX - l.TextWidth / 2, y = l.SlamY - l.TextHeight / 2;
        if (l.SlamT <= ScrawlRules.SlamTravel)
        {
            double f = Math.Min(1, dt * ScrawlRules.SlamPull);
            l.PosX += (x - l.PosX) * f; l.PosY += (y - l.PosY) * f;
            return true;
        }
        l.PosX = x; l.PosY = y;
        if (!l.SlamLanded)
        {
            l.SlamLanded = true; _scrawl.SlamLanded(l.Text, l.SlamX, l.SlamY);
            foreach (var other in Engine.Logos)
            {
                if (other == l || other.SlamT >= 0) continue;
                var (vx, vy) = ScrawlRules.Knock(other.PosX + other.TextWidth / 2, other.PosY + other.TextHeight / 2, l.SlamX, l.SlamY, other.Cruise, ScrawlMotion);
                var gain = ScrawlOptions.KnockFactor(CoreSettings.Current.ScrawlSlam);
                other.VelX += vx * gain; other.VelY += vy * gain;
            }
            double d = l.Cruise / Math.Sqrt(2);
            l.VelX = ScrawlRandom.Next(2) == 0 ? d : -d; l.VelY = ScrawlRandom.Next(2) == 0 ? d : -d;
        }
        if (l.SlamT >= ScrawlRules.SlamHold) { l.SlamT = -1; l.SlamLanded = false; }
        return true;
    }

    private static double ScrawlScale(BouncingTextEngine.Logo l) => _scrawl == null ? 1 : ScrawlRules.SlamWordScale(l.SlamT, ScrawlMotion);
}

// A scene in global DIPs, shared by the existing click-through monitor windows.
internal sealed class ScrawlScene
{
    internal sealed class Stamp
    {
        public required Geometry Geometry;
        public required StampPose Pose;
        public int Ink;
        public bool Slam;
        public double Age;
    }
    private readonly List<Stamp> _stamps = new();
    private readonly FontFamily _family;
    private readonly Random _rng = new();
    internal IReadOnlyList<Stamp> Stamps => _stamps;
    internal double FontSize { get; }
    internal string Font { get; }
    internal ScrawlScene(double size, FontFamily family) { FontSize = size; _family = family; Font = BouncingTextOverlay.Engine.Font; }
    internal void Clear() => _stamps.Clear();
    internal void Tick(double dt, double life, int cap)
    {
        foreach (var s in _stamps) s.Age += dt;
        _stamps.RemoveAll(s => s.Age >= (s.Slam ? ScrawlRules.SlamStampLife : life));
        Trim(cap);
    }
    private void Trim(int cap)
    {
        while (_stamps.Count > cap)
        {
            int oldest = _stamps.FindIndex(s => !s.Slam);
            if (oldest < 0) break;
            _stamps.RemoveAt(oldest);
        }
    }
    internal void WallHit(string word, ScrawlWall wall, double x, double y, double w, double h, ScrawlBounds b)
    {
        if (wall == ScrawlWall.None || string.IsNullOrEmpty(word)) return;
        var cx = x + w / 2; var cy = y + h / 2;
        bool corner = ScrawlRules.IsCorner(x, y, x + w, y + h, b, FontSize);
        var pose = corner ? ScrawlRules.PlaceCornerStamp(cx, cy, b, FontSize) : ScrawlRules.PlaceStamp(wall, cx, cy, b, FontSize, _rng.NextDouble() * 2 - 1);
        Add(word, pose, corner ? ScrawlRules.CornerStampScale : ScrawlRules.StampScale, corner ? 3 : ScrawlRules.InkFor(word), false);
    }
    internal void SlamLanded(string word, double x, double y) => Add(word, new StampPose(x, y, -0.14), ScrawlRules.SlamStampScale, 1, true);
    private void Add(string word, StampPose pose, double scale, int ink, bool slam)
    {
        var ft = new FormattedText(word, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(_family, FontStyle.Normal, FontWeight.Bold), FontSize * scale, Brushes.White);
        _stamps.Add(new Stamp { Geometry = ft.BuildGeometry(new Point(-ft.WidthIncludingTrailingWhitespace / 2, -ft.Height / 2)), Pose = pose, Ink = ink, Slam = slam });
        if (!slam) Trim(ScrawlOptions.ClampStampCap(CoreSettings.Current.ScrawlStampCap));
    }
}

internal sealed partial class BouncingTextOverlayWindow
{
    private ScrawlLayer? _scrawlLayer;
    internal void AttachScrawl(ScrawlScene scene)
    {
        DetachScrawl();
        _scrawlLayer = new ScrawlLayer(scene, _screen.X / _k, _screen.Y / _k) { Width = _screen.Width / _k, Height = _screen.Height / _k, IsHitTestVisible = false };
        Canvas.Children.Insert(0, _scrawlLayer);
    }
    internal void DetachScrawl() { if (_scrawlLayer != null) Canvas.Children.Remove(_scrawlLayer); _scrawlLayer = null; }
    internal void PaintScrawl() => _scrawlLayer?.InvalidateVisual();
}

internal sealed class ScrawlLayer(ScrawlScene scene, double originX, double originY) : Control
{
    /// <summary>--render-all only (RenderProof builds every view with no arguments): an empty
    /// scene at the desktop origin. Internal, so no production caller ships the sample.</summary>
    internal ScrawlLayer() : this(new ScrawlScene(48, FontFamily.Default), 0, 0) { }

    private static readonly Color[] Inks = { Color.FromRgb(255, 111, 181), Color.FromRgb(95, 255, 208), Color.FromRgb(190, 150, 255), Color.FromRgb(255, 207, 107) };
    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        var motion = AmbientFxCanvas.Env.Level;
        foreach (var s in scene.Stamps)
        {
            var life = s.Slam ? ScrawlRules.SlamStampLife : ScrawlOptions.ClampStampSeconds(CoreSettings.Current.ScrawlStampSeconds);
            var alpha = s.Slam ? ScrawlRules.StampAlpha(s.Age, life, -1) : ScrawlRules.LeftoverAlpha(s.Age, life, -1);
            var (along, across) = ScrawlRules.LandPunch(s.Age, s.Slam, motion);
            using var opacity = dc.PushOpacity(alpha);
            using var transform = dc.PushTransform(Matrix.CreateScale(across, along) * Matrix.CreateRotation(s.Pose.Rotation) * Matrix.CreateTranslation(s.Pose.X - originX, s.Pose.Y - originY));
            var color = Inks[s.Ink];
            dc.DrawGeometry(new SolidColorBrush(color, ScrawlRules.FillAlpha), new Pen(new SolidColorBrush(color, ScrawlRules.StrokeAlpha), (s.Slam ? ScrawlRules.SlamStrokeWidth : ScrawlRules.StrokeWidth) * Math.Max(1, scene.FontSize / ScrawlRules.MockupFont), lineJoin: PenLineJoin.Round), s.Geometry);
        }
    }
}
