using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Views.Overlays;
// WPF OverlayService.cs:456-465,2232-2250,2598-2610,3199-3248,3760-3764: passive blur hosts.
// Windows uses an off-thread desktop capture; other supported desktops use native blur.
internal static class BrainDrainOverlay
{
    private static readonly List<Window> Hosts = new();
    private static readonly Dictionary<Window, (BrainDrainCapturePump Pump, Image Image, WriteableBitmap? Bitmap, double Scale)> Frames = new();
    private static readonly DispatcherTimer FrameTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    internal static Func<PixelRect, BrainDrainCapturePump?> CreatePump = bounds => OperatingSystem.IsWindows()
        ? new BrainDrainCapturePump(bounds, TimeSpan.FromMilliseconds(33)) : null;
    private static Window? _owner;
    private static AppSettings? _settings;
    private static SettingsService? _service;
    private static bool? _clear;
    private static DateTime _retryAfterUtc = DateTime.MinValue; private static int _retrySeconds = 5;
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    internal static Func<Window, bool> Prepare = w => OperatingSystem.IsWindows()
        ? X11Overlay.SetClickThrough(w, true) : X11Overlay.EnableDesktopBlur(w);
    internal static Func<IReadOnlyList<Window>, IReadOnlyList<BrainDrainKeepClear.BandEntry>> ReadBand = X11Overlay.VisibleOwnBand;
    internal static Func<Window, uint, bool> Capture = Win32Overlay.SetCaptureAffinity;
    internal static Action<Window, double> Alpha = (w, a) => X11Overlay.SetOpacity(w, a);
    internal static Action<Window> Raise = w => X11Overlay.Raise(w);
    internal static Action<Window, Window> Below = (w, a) => X11Overlay.InsertBelow(w, a);
    internal static Func<Window, IReadOnlyList<(PixelRect Bounds, double Scaling, bool IsPrimary)>> Screens =
        w => ScreenList.Enumerate(w).Select(s => (s.Bounds, s.Scaling, s.IsPrimary)).ToArray();
    internal static Func<IEnumerable<Window>> OwnWindows = () =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();
    internal static Func<Window, IntPtr> Handle = w => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    internal static IReadOnlyList<Window> Active => Hosts;
    internal static bool Polling => Timer.IsEnabled;
    static BrainDrainOverlay()
    {
        Timer.Tick += (_, _) => Reconcile();
        FrameTimer.Tick += (_, _) => PresentFrames();
    }
    internal static void Start(Window owner)
    {
        if (!CoreSession.IsEngineRunning && App.Sessions?.IsRunning != true || App.Sessions?.IsPaused == true)
        { CloseAll(); return; }
        if (!ReferenceEquals(_owner, owner))
        {
            CloseAll(); _owner = owner; owner.Closed += OwnerClosed;
            _service = CoreSettings.Service;
            if (_service != null) _service.CurrentReplaced += SettingsReplaced;
            Timer.Start();
        }
        Reconcile();
    }
    private static void OwnerClosed(object? sender, EventArgs e) => CloseAll();
    private static void SettingsReplaced() => Changed(null, new PropertyChangedEventArgs(null));
    private static void Changed(object? sender, PropertyChangedEventArgs e)
    {
        void Apply() { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppSettings.AllowOverlayCapture)) { _retryAfterUtc = DateTime.MinValue; _retrySeconds = 5; } Reconcile(); }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
    internal static void Reconcile(DateTime? now = null)
    {
        if (_owner == null) return;
        if (!CoreSession.IsEngineRunning && App.Sessions?.IsRunning != true || App.Sessions?.IsPaused == true)
        { CloseAll(); return; }
        var s = CoreSettings.Current; var utcNow = now ?? DateTime.UtcNow;
        var affinity = Win32Overlay.CaptureAffinity(s.AllowOverlayCapture); // WPF OverlayCaptureAffinity.cs:27-28,51.
        if (!ReferenceEquals(s, _settings))
        {
            if (_settings != null) _settings.PropertyChanged -= Changed;
            _settings = s; s.PropertyChanged += Changed;
        }
        if (!BrainDrainVisualPolicy.WantsBlur(s.BrainDrainEnabled, s.BrainDrainBlurStrength))
        { CloseHosts(); _retryAfterUtc = DateTime.MinValue; _retrySeconds = 5; return; } // WPF :2247,2577-2579.
        if (utcNow < _retryAfterUtc) return;
        var screens = Screens(_owner);
        var targets = screens.Where(x => s.DualMonitorEnabled || x.IsPrimary).ToArray();
        if (targets.Length == 0 && screens.Count > 0) targets = new[] { screens[0] };
        // WPF OverlayService.cs:2985-3023: retain full physical bounds and scaling.
        if (Hosts.Count != targets.Length || Hosts.Where((w, i) => i < targets.Length && (!Equals(w.Tag, targets[i]) || w.Position != targets[i].Bounds.Position)).Any())
        {
            CloseHosts(); _retryAfterUtc = utcNow.AddSeconds(_retrySeconds); _retrySeconds = Math.Min(_retrySeconds * 2, 60); // WPF :3803-3810,3858-3872.
            foreach (var screen in targets)
            {
                var b = screen.Bounds; var scale = screen.Scaling > 0 ? screen.Scaling : 1;
                // WPF OverlayService.cs:2786-2830: passive per-screen, transparent, click-through.
                var w = new Window { SystemDecorations = WindowDecorations.None, ShowInTaskbar = false,
                    ShowActivated = false, Focusable = false, IsHitTestVisible = false, CanResize = false,
                    Topmost = true, Background = Brushes.Transparent,
                    TransparencyLevelHint = OperatingSystem.IsWindows() ? new[] { WindowTransparencyLevel.Transparent }
                        : new[] { WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent },
                    Tag = screen, Position = b.Position, Width = b.Width / scale, Height = b.Height / scale };
                try
                {
                    if (Capture(w, affinity))
                    {
                        w.Show();
                        if (Capture(w, affinity) && Prepare(w))
                        {
                            Hosts.Add(w);
                            var pump = CreatePump(b);
                            if (pump != null)
                            {
                                var image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
                                w.Content = image; Frames.Add(w, (pump, image, null, scale)); FrameTimer.Start();
                            }
                            continue;
                        }
                    }
                    w.Close(); CloseHosts(); Serilog.Log.Warning("Brain Drain: capture protection or compositor blur unavailable"); return;
                }
                catch { w.Close(); CloseHosts(); return; }
            }
        }
        // WPF OverlayService.cs:2814-2823,3786: creation, live change and replacement.
        foreach (var w in Hosts) if (!Capture(w, affinity)) { CloseHosts(); _retryAfterUtc = utcNow.AddSeconds(_retrySeconds); _retrySeconds = Math.Min(_retrySeconds * 2, 60); return; } _retryAfterUtc = DateTime.MinValue; _retrySeconds = 5;
        // WPF BrainDrainLayer.cs:240-256: convert the physical-pixel radius to each host's DIPs.
        foreach (var entry in Frames.Values)
            entry.Image.Effect = new BlurEffect { Radius = Math.Clamp(s.BrainDrainBlurStrength, 1, 100) * 0.14 / entry.Scale };
        // WPF Compositor/BrainDrainLayer.cs:296-301, rounded alpha at 1..100 strength.
        foreach (var w in Hosts) Alpha(w, Math.Round((.10 + .75 * Math.Pow((Math.Clamp(s.BrainDrainBlurStrength, 1, 100) - 1) / 99.0, .66)) * 255) / 255);
        // Forced off transition raises the blur again. Ordinary ticks only repair an actual drift.
        if (_clear != s.BrainDrainKeepPicturesClear)
            foreach (var w in Hosts) Raise(w);
        _clear = s.BrainDrainKeepPicturesClear;
        if (_clear != true || Hosts.Count == 0) return;
        var windows = OwnWindows().Where(w => w.IsVisible && w.Topmost).ToList();
        windows.AddRange(Hosts.Where(w => !windows.Contains(w)));
        var byHandle = windows.Where(w => Handle(w) != IntPtr.Zero).ToDictionary(Handle);
        var drains = Hosts.Select(Handle).ToHashSet();
        var band = ReadBand(windows).Select(e => e with { Drain = drains.Contains(e.Hwnd) }).ToArray();
        BrainDrainKeepClear.Sink(true, band, (w, a) => Below(byHandle[w], byHandle[a]));
    }
    // The UI only takes completed byte arrays; desktop GDI calls stay on the worker.
    internal static void PresentFrames()
    {
        foreach (var w in Frames.Keys.ToArray())
        {
            var state = Frames[w]; var frame = state.Pump.Take();
            if (frame == null) continue;
            var bitmap = state.Bitmap;
            if (bitmap == null || bitmap.PixelSize.Width != frame.Width || bitmap.PixelSize.Height != frame.Height)
            {
                var old = bitmap;
                bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                state.Image.Source = bitmap; old?.Dispose();
            }
            using (var buffer = bitmap.Lock())
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Pixels, y * frame.Width * 4, buffer.Address + y * buffer.RowBytes, frame.Width * 4);
            Frames[w] = (state.Pump, state.Image, bitmap, state.Scale);
        }
    }
    private static void CloseHosts()
    {
        FrameTimer.Stop();
        foreach (var state in Frames.Values)
        {
            state.Pump.Shutdown(); state.Image.Source = null; state.Bitmap?.Dispose();
        }
        Frames.Clear();
        foreach (var w in Hosts) w.Close();
        Hosts.Clear(); _clear = null;
    }
    internal static void CloseAll()
    {
        Timer.Stop(); _retryAfterUtc = DateTime.MinValue; _retrySeconds = 5;
        if (_settings != null) _settings.PropertyChanged -= Changed;
        if (_service != null) _service.CurrentReplaced -= SettingsReplaced;
        if (_owner != null) _owner.Closed -= OwnerClosed;
        _settings = null; _service = null; _owner = null;
        CloseHosts();
    }
}
