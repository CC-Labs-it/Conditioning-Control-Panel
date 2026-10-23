using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>One capture worker/window per screen. Stop/panic detaches immediately; workers own
/// their native cleanup even if a driver never returns. Slice B owns settings UI and keep-clear.</summary>
internal static class BrainDrainOverlay
{
    private static readonly List<(Window Window, BrainDrainVisual Visual, PixelRect Bounds)> Windows = new();
    private static readonly DispatcherTimer Poll = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static Window? _owner;
    private static BrainDrainVisual? _fallback;
    private static Panel? _fallbackParent;
    private static (int Strength, bool Melt, int Downscale, int Fps) _options;
    static BrainDrainOverlay() { Poll.Tick += (_, _) => { if (_owner != null) Refresh(_owner); }; }
    internal static int WindowCount => Windows.Count;

    internal static void Refresh(Visual host)
    {
        var s = CoreSettings.Current;
        if (App.Sessions?.IsPaused == true || !(CoreSession.IsEngineRunning || App.Sessions?.IsRunning == true)) { Stop(); return; }
        // Keep the 2 Hz owner alive while the engine runs: enabling a previously off feature,
        // replacing CoreSettings and display hotplug are all picked up without leaking subscriptions.
        if (_owner == null && TopLevel.GetTopLevel(host) is Window owner)
        { _owner = owner; owner.Closed += OwnerClosed; Poll.Start(); }
        if (!BrainDrainVisualPolicy.WantsBlur(s.BrainDrainEnabled, s.BrainDrainBlurStrength)) { Clear(); return; }
        var screens = ScreenList.Enumerate(host);
        int primary = screens.ToList().FindIndex(x => x.IsPrimary);
        var indices = PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.GlobalTargetMonitor,
            s.DualMonitorEnabled, screens.Count, primary);
        var bounds = indices.Select(i => screens[i].Bounds).ToArray();
        var tier = AmbientFxCanvas.Env.CurrentTier;
        var options = (s.BrainDrainBlurStrength, s.BrainDrainMeltEnabled, tier == PerformanceTier.Performance ? 8 : 4,
            Math.Min(s.BrainDrainHighRefresh ? 60 : 30, tier == PerformanceTier.Performance ? 15 : tier == PerformanceTier.Balanced ? 20 : 30));
        if (Windows.Count > 0 && Windows.Select(w => w.Bounds).SequenceEqual(bounds))
        {
            if (_options != options)
            {
                _options = options;
                foreach (var w in Windows) w.Visual.Update(Input(w.Bounds));
            }
            return;
        }
        if (_fallback != null) return;
        Clear(); _options = options;
        var backend = X11Overlay.BackendOf(_owner?.TryGetPlatformHandle());
        if (backend == OverlayBackend.None || !X11Overlay.IsAvailable || !X11Overlay.IsCompositing || bounds.Length == 0)
        { ShowInAppFallback(); return; }
        foreach (var b in bounds)
        {
            var visual = new BrainDrainVisual();
            var window = new Window
            {
                SystemDecorations = WindowDecorations.None, TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                CanResize = false, Focusable = false, IsHitTestVisible = false, Content = visual,
            };
            Windows.Add((window, visual, b));
            X11Overlay.SetOverrideRedirect(window, b);
            if (!X11Overlay.SetClickThrough(window, true)) { Clear(); ShowInAppFallback(); return; }
            window.Show();
            if (window.ActualTransparencyLevel == WindowTransparencyLevel.None) { Clear(); ShowInAppFallback(); return; }
        }
        // Every overlay ID exists before any capture starts; each worker excludes ALL of them.
        foreach (var w in Windows) w.Visual.Start(Input(w.Bounds), DesktopCapture.Factory(backend));
    }
    private static BrainDrainCapturePump.Input Input(PixelRect bounds) => new(bounds, _options.Strength, _options.Melt,
        _options.Downscale, _options.Fps, Windows.Select(w => w.Window.TryGetPlatformHandle()!.Handle).ToArray());
    private static void ShowInAppFallback()
    {
        // ponytail: native Wayland has no safe global click-through overlay. The documented veil
        // is confined to our shell, with a persistent, truthful notice; no desktop capture attempt.
        if (_owner?.Content is not Panel parent) return;
        _fallbackParent = parent;
        _fallback = new BrainDrainVisual { ZIndex = int.MaxValue };
        Grid.SetRowSpan(_fallback, 100); Grid.SetColumnSpan(_fallback, 100);
        parent.Children.Add(_fallback);
        _fallback.Start(new(default, 1, false, 4, 15, Array.Empty<IntPtr>()), null);
        _fallback.SetNotice("Brain Drain: desktop blur unavailable. Flat veil shown in this app only.");
    }
    private static void Clear()
    {
        foreach (var w in Windows)
        {
            w.Visual.Dispose();
            try { w.Window.Close(); } catch (Exception ex) { Log.Warning(ex, "Brain Drain window close failed"); }
        }
        Windows.Clear();
        if (_fallback != null) { _fallback.Dispose(); _fallbackParent?.Children.Remove(_fallback); }
        _fallback = null; _fallbackParent = null;
    }
    internal static void Stop()
    {
        Poll.Stop();
        if (_owner != null) _owner.Closed -= OwnerClosed;
        _owner = null;
        Clear();
    }
    private static void OwnerClosed(object? sender, EventArgs e) => Stop();
}

internal sealed class BrainDrainVisual : Grid, IDisposable
{
    // FirstShowEffects.DrainAlpha's no-capture fallback, including its milky RGB.
    internal const byte VeilAlpha = 70;
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    internal readonly Border Veil = new() { Background = new SolidColorBrush(Color.FromArgb(VeilAlpha, 236, 228, 244)) };
    internal readonly TextBlock Notice = new() { Foreground = Brushes.White, Background = Brushes.Black,
        TextWrapping = TextWrapping.Wrap, MaxLines = 3, Margin = new Thickness(24),
        VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _tick = new();
    private Bitmap? _bitmap;
    internal BrainDrainCapturePump? Pump { get; private set; }
    internal BrainDrainVisual()
    {
        IsHitTestVisible = false;
        Children.Add(_image); Children.Add(Veil); Children.Add(Notice);
        _tick.Tick += (_, _) => Step();
    }
    internal void Start(BrainDrainCapturePump.Input input, Func<IDesktopCapture>? factory, bool startWorker = true)
    {
        Dispose();
        Pump = new(input, factory, start: startWorker);
        Update(input);
        SetNotice(Pump.Notice ?? "Brain Drain: waiting for desktop capture. Showing a flat veil.");
        // Unsupported backend is static: no timer or worker to keep composition alive.
        if (factory != null) _tick.Start();
    }
    internal void Update(BrainDrainCapturePump.Input input)
    {
        Pump?.Update(input);
        _image.Opacity = BrainDrainCapturePump.Alpha(input.Strength);
        _tick.Interval = TimeSpan.FromSeconds(1.0 / input.Fps);
    }
    internal void SetNotice(string? message)
    {
        if (Notice.Text != message && message != null) Log.Warning("{BrainDrainNotice}", message);
        if (message == null && Notice.Text != null) Log.Information("Brain Drain: fresh desktop blur frame displayed");
        Notice.Text = message; Notice.IsVisible = message != null; Veil.IsVisible = message != null;
    }
    internal void Step()
    {
        if (Pump == null) return;
        using var frame = Pump.Take();
        if (frame != null)
        {
            using var pixels = frame.Image.PeekPixels();
            var bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixels.GetPixels(),
                new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), pixels.RowBytes);
            _image.Source = bitmap; _bitmap?.Dispose(); _bitmap = bitmap;
            SetNotice(null);
        }
        if (Pump.Notice is { } failure)
        {
            _image.Source = null; _bitmap?.Dispose(); _bitmap = null;
            SetNotice(failure);
        }
    }
    public void Dispose()
    {
        _tick.Stop(); Pump?.Dispose(); Pump = null;
        _image.Source = null; _bitmap?.Dispose(); _bitmap = null;
    }
}
