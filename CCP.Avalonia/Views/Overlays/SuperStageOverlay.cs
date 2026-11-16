using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using SuperStageHost = ConditioningControlPanel.Avalonia.Platform.Super.SuperStageHost;
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
    internal static Func<Window, bool> Raise = X11Overlay.Raise;
    internal static Func<SuperEffect, bool> Replaces = SuperAccess.ReplacesBase;
    internal static Func<bool> Composited = () => X11Overlay.IsAvailable && X11Overlay.IsCompositing;
    internal static Func<Window, PixelRect, bool> Configure = (w, bounds) => X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);
    internal static IReadOnlyList<PixelRect>? ScreensForTests;
    internal static IReadOnlyList<SuperStageOverlay> Windows => Hosts;
    private static readonly List<SuperStageOverlay> Hosts = new();
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private static readonly DispatcherTimer RestoreTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private static long _restoreAt;
    private static bool _restoreHooked;
    private static readonly WhirlpoolDraw PoolDraw = new();
    private static readonly LoomMorph Morph = new(new Random());
    private static double _phase, _steamAlpha, _poolAlpha;
    private static bool _steamShown, _poolShown;
    private static bool _lastUnder;
    private static string _pinKey = "";
    private static bool _hooked;
    private static Window[] _orderedEffects = Array.Empty<Window>();
    private readonly SteamDraw _fog = new();
    private readonly SteamWords _words = new();
    private readonly SteamWordsDraw _wordsDraw = new();
    private double _lastWordsTime = -1, _poolReadAt = -1;
    private bool _poolOnly;
    internal static string? Line { get; set; }
    internal static Func<bool> AfterglowOn = () => SuperAccess.IsOn(SuperEffect.Afterglow);
    private static readonly AfterglowLine Afterglow = new();

    internal static void WordShown(string text)
    {
        if (!SuperStageHost.EngineRunning() || !SuperStageHost.Stage.SteamOn || Hosts.Count == 0 || !AfterglowOn()) return;
        var set = CoreSettings.Current;
        Afterglow.Words = AfterglowLineOptions.ClampWords(set.AfterglowLineWords);
        Afterglow.HoldSeconds = AfterglowLineOptions.ClampHold(set.AfterglowLineHold);
        Afterglow.Add(text.ToLowerInvariant());
    }

    internal static void StepAfterglow(double dt)
    {
        if (!AfterglowOn()) { Afterglow.Clear(); return; }
        var done = Afterglow.Step(dt);
        if (done == null) return;
        Line = done;
        int primary = 0;
        if (Hosts.Count > 0)
            for (int i = 0; i < Hosts[0].Screens.All.Count; i++) if (Hosts[0].Screens.All[i].IsPrimary) { primary = i; break; }
        var set = CoreSettings.Current;
        var targets = PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, set.GlobalTargetMonitor, set.DualMonitorEnabled, Hosts.Count / 2, primary);
        var host = Hosts.FirstOrDefault(w => !w._poolOnly && targets.Contains(w._index));
        if (host == null) return;
        var b = host._screen;
        SuperStageHost.Stage.Remember(b.X + b.Width * 0.06, b.Y + b.Height * 0.38, b.X + b.Width * 0.94, b.Y + b.Height * 0.66);
    }
    private static readonly SKTypeface Face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
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
        RestoreTimer.Stop();
        bool steam = SuperStageHost.Stage.SteamOn, pool = SuperStageHost.Stage.PoolOn;
        if (steam && !_steamShown) _steamAlpha = GentleVisuals.On ? 0 : 1;
        if (pool && !_poolShown) _poolAlpha = GentleVisuals.On ? 0 : 1;
        if (!steam && _steamShown)
        {
            Line = null; Afterglow.Clear();
            foreach (var w in Hosts) { w._poolReadAt = -1; w._lastWordsTime = -1; }
        }
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
                // WPF SteamLayer.cs:45 and WhirlpoolLayer.cs:22 occupy distinct z slots.
                for (int layer = 0; layer < 2; layer++)
                {
                    var w = i == 0 && layer == 0 ? first : new SuperStageOverlay();
                    Hosts.Add(w);
                    w._poolOnly = layer == 1;
                    w._screen = screens[i]; w._index = i;
                    w._scale = i < first.Screens.All.Count ? first.Screens.All[i].Scaling : 1;
                    SuperStageHost.NoteScreen(w._screen, w._scale);
                    if (!Configure(w, w._screen)) { CloseAll(); Timer.Start(); return; }
                    w.Show();
                }
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

    internal static void Tick(object? sender, EventArgs e)
    {
        if (!SuperStageHost.EngineRunning() || (!SuperStageHost.Stage.SteamOn && !SuperStageHost.Stage.PoolOn)) { CloseAll(); return; }
        if (Hosts.Count != 0 && !Composited()) { CloseAll(); Timer.Start(); }
        SuperStageHost.Step();
        double dt = SuperStageHost.LastDt;
        StepAfterglow(dt);
        PinkFilterOverlay.StepSuperAside(Hosts.Count > 0 && SuperStageHost.Stage.SteamOn && Replaces(SuperEffect.Creep), dt);
        SpiralOverlay.StepSuperAside(Hosts.Count > 0 && SuperStageHost.Stage.PoolOn && Replaces(SuperEffect.Vortex), dt);
        if (GentleVisuals.On)
        {
            double fade = GentleVisuals.FadeSeconds;
            _steamAlpha = Math.Min(1, _steamAlpha + dt / fade);
            _poolAlpha = Math.Min(1, _poolAlpha + dt / fade);
        }
        else _steamAlpha = _poolAlpha = 1;
        if (Hosts.Count > 0 && (_lastUnder != CoreSettings.Current.PinkFilterUnderEffects || !_orderedEffects.SequenceEqual(PinkFilterOverlay.EffectWindows())))
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

    internal static void Reorder()
    {
        _orderedEffects = PinkFilterOverlay.EffectWindows().ToArray();
        foreach (var w in PinkFilterOverlay.Shown) Raise(w);
        foreach (var w in Hosts.Where(w => !w._poolOnly)) Raise(w);
        if (CoreSettings.Current.PinkFilterUnderEffects)
            foreach (var w in _orderedEffects) Raise(w);
        RaisePool();
    }

    internal static void RaisePool()
    {
        foreach (var w in Hosts.Where(w => w._poolOnly)) Raise(w);
    }

    internal static void CloseAll()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(CloseAll); return; }
        Timer.Stop();
        RestoreTimer.Stop();
        if (SuperStageHost.EngineRunning() && !SuperStageHost.Stage.SteamOn && !SuperStageHost.Stage.PoolOn)
        {
            if (!_restoreHooked) { RestoreTimer.Tick += RestoreTick; _restoreHooked = true; }
            _restoreAt = Stopwatch.GetTimestamp();
            if (PinkFilterOverlay.SuperVeil < 1 || SpiralOverlay.SuperVeil < 1) RestoreTimer.Start();
        }
        else StepRestore(10);
        _steamShown = _poolShown = false;
        _orderedEffects = Array.Empty<Window>();
        Line = null; Afterglow.Clear();
        if (Hosts.Count == 0) return;
        Hosts[0].Screens.Changed -= ScreensChanged;
        var all = Hosts.ToArray(); Hosts.Clear();
        foreach (var w in all)
        {
            if (!w._disposed) { w._fog.Dispose(); w._wordsDraw.Dispose(); w._disposed = true; }
            try { w.Close(); } catch (Exception ex) { Log.Debug("Super stage close: {E}", ex.Message); }
        }
    }

    private static void RestoreTick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp((now - _restoreAt) / (double)Stopwatch.Frequency, 0, 0.1);
        _restoreAt = now;
        StepRestore(SuperStageHost.EngineRunning() ? dt : 10);
    }

    internal static void StepRestore(double dt)
    {
        PinkFilterOverlay.StepSuperAside(false, dt);
        SpiralOverlay.StepSuperAside(false, dt);
        if (PinkFilterOverlay.SuperVeil >= 1 && SpiralOverlay.SuperVeil >= 1) RestoreTimer.Stop();
    }

    private sealed class StageSurface(SuperStageOverlay host) : Control
    {
        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            var age = SuperStageHost.SteamAge;
            if (!host._poolOnly && (host._poolReadAt < 0 || age - host._poolReadAt > 50))
            {
                host._poolReadAt = age;
                try { host._words.SetPool(CoreSettings.Current.SubliminalPool.Where(x => x.Value && x.Key.Length is > 0 and <= 16).Select(x => x.Key.ToLowerInvariant())); }
                catch (Exception ex) { Log.Debug("Steam words: {E}", ex.Message); }
            }
            double dt = host._lastWordsTime < 0 ? 0 : Math.Clamp(age - host._lastWordsTime, 0, 0.1);
            host._lastWordsTime = age;
            RenderMonitor(canvas, host._fog, host._screen, bounds.Size, host._index, host._scale, host._poolOnly, host._words, host._wordsDraw, dt);
            context.Custom(new PictureOp(recorder.EndRecording(), bounds));
        }
    }

    internal static void RenderMonitor(SKCanvas canvas, SteamDraw fog, PixelRect screen, Size surface, int index, double scale, bool? poolOnly = null, SteamWords? words = null, SteamWordsDraw? wordsDraw = null, double dt = 0)
    {
        if (screen.Width <= 0 || screen.Height <= 0) return;
        var set = CoreSettings.Current;
        int count = Math.Max(Hosts.Count / 2, index + 1);
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
        if (poolOnly != true && stage.SteamOn && steamTarget)
        {
            var field = stage.Field(screen.ToString(), screen.X, screen.Y, screen.Width, screen.Height);
            field.Heal = SteamOptions.HealRate(set.SteamFogReturn);
            field.Drift = SteamOptions.Drift(set.SteamDrift);
            var (r, g, b) = PinkFilterOverlay.EffectiveColor();
            double strength = Math.Clamp(0.3 + PinkFilterOverlay.PaintedOpacity * 1.6, 0.3, 0.82);
            using var steamPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * _steamAlpha)) };
            canvas.SaveLayer(steamPaint);
            var bounds = new SKRect(screen.X, screen.Y, screen.Right, screen.Bottom);
            var colour = new SKColor(r, g, b);
            fog.Draw(canvas, bounds, field, colour, strength, Array.Empty<string>(), null, Face, SuperStageHost.SteamAge);
            if (words != null && wordsDraw != null)
            {
                words.Line = Line;
                words.Step(field, dt, GentleVisuals.On, set.SteamWordsInFog);
                wordsDraw.Draw(canvas, bounds, field, words, colour, Face);
            }
            canvas.Restore();
        }
        if (poolOnly != false && stage.PoolOn && poolTarget)
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
