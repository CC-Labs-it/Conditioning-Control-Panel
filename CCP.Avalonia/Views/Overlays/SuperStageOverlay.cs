using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform.Super;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>Transparent, input-free per-monitor surfaces for the shared Super stage.</summary>
internal sealed class SuperStageOverlay : Window
{
    internal static Func<bool> Composited = () => X11Overlay.IsAvailable && X11Overlay.IsCompositing;
    internal static Func<Window, PixelRect, bool> Configure = (w, bounds) => X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);
    internal static IReadOnlyList<PixelRect>? ScreensForTests;
    internal static IReadOnlyList<SuperStageOverlay> Windows => Hosts;
    private static readonly List<SuperStageOverlay> Hosts = new();
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private static readonly WhirlpoolDraw PoolDraw = new();
    private static readonly LoomMorph Morph = new(new Random());
    private static double _phase, _steamAlpha, _poolAlpha;
    private static bool _steamShown, _poolShown;
    private static bool _lastUnder;
    private static string _pinKey = "";
    private static bool _hooked;
    private readonly SteamDraw _fog = new();
    private PixelRect _screen;
    private int _index;
    private double _scale;
    private bool _disposed;

    private SuperStageOverlay()
    {
        SystemDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        CanResize = false; Focusable = false; IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = new StageSurface(this);
    }

    internal static void Sync()
    {
        if (!SuperStageHost.Stage.SteamOn && !SuperStageHost.Stage.PoolOn) { CloseAll(); return; }
        bool steam = SuperStageHost.Stage.SteamOn, pool = SuperStageHost.Stage.PoolOn;
        if (steam && !_steamShown) _steamAlpha = GentleVisuals.On ? 0 : 1;
        if (pool && !_poolShown) _poolAlpha = GentleVisuals.On ? 0 : 1;
        _steamShown = steam; _poolShown = pool;
        if (!_hooked) { Timer.Tick += Tick; _hooked = true; }
        Timer.Start(); // The stage clock also runs when there is no desktop-wide surface.
        if (Hosts.Count != 0) return;
        if (!Composited()) return;
        var first = new SuperStageOverlay();
        try
        {
            var screens = ScreensForTests ?? first.Screens.All.Select(s => s.Bounds).ToArray();
            if (screens.Count == 0) { first.Close(); return; }
            SuperStageHost.ClearScreens();
            for (int i = 0; i < screens.Count; i++)
            {
                var w = i == 0 ? first : new SuperStageOverlay();
                Hosts.Add(w);
                w._screen = screens[i]; w._index = i;
                w._scale = i < first.Screens.All.Count ? first.Screens.All[i].Scaling : 1;
                SuperStageHost.NoteScreen(w._screen, w._scale);
                if (!Configure(w, w._screen)) { CloseAll(); Timer.Start(); return; }
                w.Show();
            }
            first.Screens.Changed += ScreensChanged;
            _lastUnder = CoreSettings.Current.PinkFilterUnderEffects;
            Reorder();
        }
        catch (Exception ex) { Log.Warning(ex, "Super stage surface failed"); CloseAll(); try { first.Close(); } catch { } }
    }

    private static void ScreensChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (Hosts.Count == 0) return;
        CloseAll(); Sync();
    });

    private static void Tick(object? sender, EventArgs e)
    {
        if (!SuperStageHost.EngineRunning() || (!SuperStageHost.Stage.SteamOn && !SuperStageHost.Stage.PoolOn)) { CloseAll(); return; }
        if (Hosts.Count != 0 && !Composited()) { CloseAll(); Timer.Start(); }
        SuperStageHost.Step();
        double dt = SuperStageHost.LastDt;
        if (GentleVisuals.On)
        {
            double fade = GentleVisuals.FadeSeconds;
            _steamAlpha = Math.Min(1, _steamAlpha + dt / fade);
            _poolAlpha = Math.Min(1, _poolAlpha + dt / fade);
        }
        else _steamAlpha = _poolAlpha = 1;
        if (_lastUnder != CoreSettings.Current.PinkFilterUnderEffects)
        { _lastUnder = CoreSettings.Current.PinkFilterUnderEffects; Reorder(); }
        var key = CoreSettings.Current.WhirlpoolSpiral ?? "";
        if (_pinKey != key)
        {
            _pinKey = key;
            LoomSpec? pin = null;
            foreach (var (slug, _, json) in ConditioningControlPanel.Services.Chaos.DtrhLoomStore.List())
                if (slug == key) { pin = LoomSpecJson.Parse(json); break; }
            Morph.Pin(pin);
        }
        if (SuperStageHost.Stage.Pool.Size > 0.004)
        {
            bool gentle = GentleVisuals.On;
            Morph.Hold = WhirlpoolOptions.ClampHold(CoreSettings.Current.WhirlpoolNewEvery);
            Morph.Step(gentle ? dt * 0.6 : dt);
            _phase += dt * (gentle ? 0.06 : 0.12);
        }
        foreach (var host in Hosts) (host.Content as Control)?.InvalidateVisual();
    }

    private static void Reorder()
    {
        foreach (var w in Hosts) X11Overlay.Raise(w);
        if (CoreSettings.Current.PinkFilterUnderEffects)
            foreach (var w in PinkFilterOverlay.EffectWindows()) X11Overlay.Raise(w);
        else PinkFilterOverlay.SyncOrder();
    }

    internal static void CloseAll()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(CloseAll); return; }
        Timer.Stop();
        _steamShown = _poolShown = false;
        if (Hosts.Count == 0) return;
        Hosts[0].Screens.Changed -= ScreensChanged;
        var all = Hosts.ToArray(); Hosts.Clear();
        foreach (var w in all)
        {
            if (!w._disposed) { w._fog.Dispose(); w._disposed = true; }
            try { w.Close(); } catch (Exception ex) { Log.Debug("Super stage close: {E}", ex.Message); }
        }
    }

    private sealed class StageSurface(SuperStageOverlay host) : Control
    {
        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            RenderMonitor(canvas, host._fog, host._screen, bounds.Size, host._index, host._scale);
            context.Custom(new PictureOp(recorder.EndRecording(), bounds));
        }
    }

    internal static void RenderMonitor(SKCanvas canvas, SteamDraw fog, PixelRect screen, Size surface, int index, double scale)
    {
        if (screen.Width <= 0 || screen.Height <= 0) return;
        var set = CoreSettings.Current;
        int count = Math.Max(Hosts.Count, index + 1);
        int primary = 0;
        if (Hosts.Count > 0)
            for (int i = 0; i < Hosts[0].Screens.All.Count; i++) if (Hosts[0].Screens.All[i].IsPrimary) { primary = i; break; }
        bool steamTarget = PinkFilterOverlay.ResolveScreenIndices(set.PinkFilterTargetMonitor, set.GlobalTargetMonitor, set.DualMonitorEnabled, count, primary).Contains(index);
        bool poolTarget = PinkFilterOverlay.ResolveScreenIndices(set.SpiralTargetMonitor, set.GlobalTargetMonitor, set.DualMonitorEnabled, count, primary).Contains(index);
        SuperStageHost.NoteScreen(screen, scale);
        canvas.Save();
        canvas.Scale((float)(surface.Width / screen.Width), (float)(surface.Height / screen.Height));
        canvas.Translate(-screen.X, -screen.Y);
        var stage = SuperStageHost.Stage;
        if (stage.SteamOn && steamTarget)
        {
            var field = stage.Field(screen.ToString(), screen.X, screen.Y, screen.Width, screen.Height);
            field.Heal = SteamOptions.HealRate(set.SteamFogReturn);
            field.Drift = SteamOptions.Drift(set.SteamDrift);
            var (r, g, b) = PinkFilterOverlay.EffectiveColor();
            double strength = Math.Clamp(0.3 + PinkFilterOverlay.PaintedOpacity * 1.6, 0.3, 0.82);
            using var steamPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * _steamAlpha)) };
            canvas.SaveLayer(steamPaint);
            fog.Draw(canvas, new SKRect(screen.X, screen.Y, screen.Right, screen.Bottom), field, new SKColor(r, g, b), strength, Array.Empty<string>(), null, null, SuperStageHost.SteamAge);
            canvas.Restore();
        }
        if (stage.PoolOn && poolTarget)
        {
            using var poolPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * _poolAlpha)) };
            canvas.SaveLayer(poolPaint);
            PoolDraw.Draw(canvas, stage.Pool, Math.Clamp(set.SpiralOpacity / 100.0, 0, 1), Morph.From, Morph.To, Morph.Mix, _phase);
            canvas.Restore();
        }
        canvas.Restore();
    }

    private sealed class PictureOp(SKPicture picture, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() => picture.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature == null) return;
            using var lease = feature.Lease();
            lease.SkCanvas.DrawPicture(picture);
        }
    }
}
