using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.UI;
using SkiaSharp;
namespace ConditioningControlPanel.Avalonia.Views.Overlays;
// WPF EffectPayloadFiring.cs:70-99, OverlayService.cs:900-917,2234-2250 and
// BrainDrainLayer.cs:104-121,238-266,296-301. The unchanged WPF pump captures off-thread.
internal static class BrainDrainOverlay
{
    private static readonly List<DrainWindow> Windows = new();
    private static BrainDrainCapturePump? _pump;
    private static FrameClock? _frames;
    private static Window? _owner;
    private static int _generation;
    private static long _started, _ending;
    private static bool _allowCapture;
    internal static bool IsShowing { get; private set; }
    // Test seams: the real path opens native windows and schedules its end.
    internal static Func<int, bool, bool>? ShowOverride;
    internal static Func<bool> UserDrainRunning = () => false; // No base visual consumer on this head yet.
    internal static Action<Action, TimeSpan> Schedule = (a, t) => DispatcherTimer.RunOnce(a, t);
    internal static void Seed() => EffectPayload.Firer = FirePayload;
    internal static void FirePayload(EffectPayload payload)
    {
        if (payload is not BrainDrainMeltPayload)
        {
            Serilog.Log.Information("Chaos payload {Payload} not fired: no effect runner on this head yet", payload.DisplayName);
            return;
        }
        if (!Dispatcher.UIThread.CheckAccess())
        {
            int gen = _generation;
            Dispatcher.UIThread.Post(() => { if (gen == _generation) FirePayload(payload); });
            return;
        }
        var s = CoreSettings.Current;
        if (PanicAftermath.TriggersSilenced() || !BrainDrainBubble.ShouldPlayOverlay(IsShowing, UserDrainRunning())
            || BrainDrainVisualPolicy.IsSilent(s.BrainDrainBlurStrength)) return;
        bool melt = BrainDrainBubble.OverlayKindFor(AmbientFxCanvas.Env.Level) == BrainDrainBubble.MeltKind;
        if (!(ShowOverride?.Invoke(s.BrainDrainBlurStrength, melt) ?? Show(s.BrainDrainBlurStrength, melt))) return;
        IsShowing = true; _started = Stopwatch.GetTimestamp(); _ending = 0;
        int generation = System.Threading.Interlocked.Increment(ref _generation);
        Schedule(() => { if (generation == _generation) End(); }, TimeSpan.FromMilliseconds(BrainDrainBubble.OverlayMs));
    }
    private static bool Show(int strength, bool melt)
    {
        if (!OperatingSystem.IsWindows() || MainShellWindow.Current is not { } owner) return false;
        var s = CoreSettings.Current;
        var screens = ScreenList.Enumerate(owner);
        int primary = -1;
        for (int i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }
        var targets = PinkFilterOverlay.ResolveScreenIndices(MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
        if (targets.Length == 0) return false;
        try
        {
            foreach (int i in targets)
            {
                var w = new DrainWindow(screens[i], strength); Windows.Add(w); w.Show();
                // Refuse a full-screen window if click-through or capture privacy is unavailable.
                if (!X11Overlay.SetClickThrough(w, true) || w.ActualTransparencyLevel == WindowTransparencyLevel.None
                    || w.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } h
                    || !SetWindowDisplayAffinity(h.Handle, s.AllowOverlayCapture ? 0u : 0x11u))
                { Stop(); return false; }
            }
            var rects = targets.Select(i => screens[i].Bounds)
                .Select(b => new System.Drawing.Rectangle(b.X, b.Y, b.Width, b.Height)).ToArray();
            const int downscale = 4;
            float sigma = (float)(strength * 0.14 / downscale / 3.0);
            float amplitude = (float)(1 + Math.Clamp(strength, 0, 200) * .045);
            var interval = FrameInterval(s.BrainDrainHighRefresh);
            _pump = new BrainDrainCapturePump(downscale, interval, melt, rects, sigma, amplitude);
            StartFrames(new FrameClock(Windows[0]), interval);
            _allowCapture = s.AllowOverlayCapture;
            _owner = owner; owner.Closed += OwnerClosed;
            return true;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain overlay failed"); Stop(); return false; }
    }
    internal static TimeSpan FrameInterval(bool highRefresh) => TimeSpan.FromSeconds(1.0 / (highRefresh ? 60 : 30));
    internal static void StartFrames(FrameClock clock, TimeSpan interval)
    {
        _frames = clock; clock.Interval = interval; clock.Tick += OnFrame; clock.Start();
    }
    private static void End()
    {
        if (GentleVisuals.On && Windows.Count > 0) _ending = Stopwatch.GetTimestamp();
        else Stop();
    }
    private static void OnFrame(object? sender, EventArgs e)
    {
        if (_pump is not { } pump) return;
        if (pump.FramesPublished == 0 && Stopwatch.GetElapsedTime(_started).TotalSeconds > 3)
        { Serilog.Log.Warning("Brain Drain capture produced no frames"); Stop(); return; }
        bool allow = CoreSettings.Current.AllowOverlayCapture;
        if (allow != _allowCapture)
        {
            foreach (var w in Windows)
                if (w.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } h && !SetWindowDisplayAffinity(h.Handle, allow ? 0u : 0x11u))
                { Stop(); return; }
            _allowCapture = allow;
        }
        if (_ending != 0 && !GentleVisuals.On) { Stop(); return; }
        double fade = !GentleVisuals.On ? 1 : _ending == 0
            ? Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalSeconds / GentleVisuals.FadeSeconds, 0, 1)
            : 1 - Math.Clamp(Stopwatch.GetElapsedTime(_ending).TotalSeconds / GentleVisuals.FadeSeconds, 0, 1);
        if (_ending != 0 && fade <= 0) { Stop(); return; }
        foreach (var w in Windows)
        {
            w.SetFade(fade);
            if (pump.TryTakeFrame(w.CenterX, w.CenterY, out var frame, out _) && frame != null)
                using (frame) w.UpdateFrame(frame);
        }
    }
    private static void OwnerClosed(object? sender, EventArgs e) => Stop();
    internal static void Stop()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            System.Threading.Interlocked.Increment(ref _generation);
            System.Threading.Interlocked.Exchange(ref _pump, null)?.Shutdown();
            Dispatcher.UIThread.Post(Stop);
            return;
        }
        System.Threading.Interlocked.Increment(ref _generation); IsShowing = false;
        if (_frames != null) { _frames.Stop(); _frames.Tick -= OnFrame; _frames = null; }
        _pump?.Shutdown(); _pump = null; // Never join a stalled desktop capture on the UI thread.
        foreach (var w in Windows) { try { w.Close(); } catch { } }
        Windows.Clear();
        if (_owner != null) _owner.Closed -= OwnerClosed;
        _owner = null;
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    private sealed class DrainWindow : Window
    {
        private readonly Image _image;
        private readonly double _alpha;
        private WriteableBitmap? _bitmap;
        internal int CenterX { get; }
        internal int CenterY { get; }
        internal DrainWindow(Screen screen, int strength)
        {
            var b = screen.Bounds;
            CenterX = b.X + b.Width / 2; CenterY = b.Y + b.Height / 2; Position = new PixelPoint(b.X, b.Y);
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            Width = b.Width / scale; Height = b.Height / scale;
            SystemDecorations = WindowDecorations.None; WindowStartupLocation = WindowStartupLocation.Manual;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; CanResize = false; Focusable = false; IsHitTestVisible = false;
            double t = (Math.Clamp(strength, 1, 100) - 1) / 99.0;
            _alpha = .10 + .75 * Math.Pow(t, .66);
            _image = new Image { Stretch = Stretch.Fill, Opacity = GentleVisuals.On ? 0 : _alpha }; Content = _image;
            Closed += (_, _) => { _image.Source = null; _bitmap?.Dispose(); _bitmap = null; };
        }
        internal void SetFade(double fade) => _image.Opacity = _alpha * fade;
        internal void UpdateFrame(SKImage frame)
        {
            using var pixels = frame.PeekPixels();
            if (pixels == null) return;
            if (_bitmap == null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
            {
                _image.Source = null; _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                _image.Source = _bitmap;
            }
            using var fb = _bitmap.Lock();
            pixels.ReadPixels(new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul), fb.Address, fb.RowBytes);
            _image.InvalidateVisual();
        }
    }
}
