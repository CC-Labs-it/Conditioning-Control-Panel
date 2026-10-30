using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF OverlayService.cs:456-465,2232-2250,2598-2610,3199-3248,3760-3764.
// The compositor blurs the scene behind each host. No captured desktop or feedback copy exists.
// KWin/X11 and Windows native blur only; other compositors fail closed. Native kernels differ
// from WPF's radius curve; melt and capture-based rendering remain outside this keep-clear port.
internal static class BrainDrainOverlay
{
    private static readonly List<Window> Hosts = new();
    private static Window? _owner;
    private static AppSettings? _settings;
    private static SettingsService? _service;
    private static bool? _clear;
    private static bool _refused;
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    internal static Func<Window, bool> Prepare = X11Overlay.EnableDesktopBlur;
    internal static Func<IReadOnlyList<Window>, IReadOnlyList<BrainDrainKeepClear.BandEntry>> ReadBand = X11Overlay.VisibleOwnBand;
    internal static Action<Window, double> Alpha = (w, a) => X11Overlay.SetOpacity(w, a);
    internal static Action<Window> Raise = w => X11Overlay.Raise(w);
    internal static Action<Window, Window> Below = (w, a) => X11Overlay.RestackBelow(w, a);
    internal static Func<Window, IReadOnlyList<(PixelRect Bounds, double Scaling, bool IsPrimary)>> Screens =
        w => ScreenList.Enumerate(w).Select(s => (s.Bounds, s.Scaling, s.IsPrimary)).ToArray();
    internal static Func<IEnumerable<Window>> OwnWindows = () =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();
    internal static Func<Window, IntPtr> Handle = w => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    internal static IReadOnlyList<Window> Active => Hosts;
    internal static bool Polling => Timer.IsEnabled;
    static BrainDrainOverlay() => Timer.Tick += (_, _) => Reconcile();

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
        if (Dispatcher.UIThread.CheckAccess()) Reconcile();
        else Dispatcher.UIThread.Post(() => Reconcile());
    }
    internal static void Reconcile()
    {
        if (_owner == null) return;
        if (!CoreSession.IsEngineRunning && App.Sessions?.IsRunning != true || App.Sessions?.IsPaused == true)
        { CloseAll(); return; }
        var s = CoreSettings.Current;
        if (!ReferenceEquals(s, _settings))
        {
            if (_settings != null) _settings.PropertyChanged -= Changed;
            _settings = s; s.PropertyChanged += Changed;
        }
        if (!BrainDrainVisualPolicy.WantsBlur(s.BrainDrainEnabled, s.BrainDrainBlurStrength))
        { CloseHosts(); return; } // WPF :2247: zero means no picture, not a faint haze.
        if (_refused) return;
        var screens = Screens(_owner);
        var targets = screens.Where(x => s.DualMonitorEnabled || x.IsPrimary).ToArray();
        if (targets.Length == 0 && screens.Count > 0) targets = new[] { screens[0] };
        if (Hosts.Count != targets.Length || Hosts.Where((w, i) => i < targets.Length && w.Position != targets[i].Bounds.Position).Any())
        {
            CloseHosts();
            foreach (var screen in targets)
            {
                var b = screen.Bounds; var scale = screen.Scaling > 0 ? screen.Scaling : 1;
                // WPF OverlayService.cs:2786-2830: passive per-screen, transparent, click-through.
                var w = new Window { SystemDecorations = WindowDecorations.None, ShowInTaskbar = false,
                    ShowActivated = false, Focusable = false, IsHitTestVisible = false, CanResize = false,
                    Topmost = true, Background = Brushes.Transparent,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent },
                    Position = b.Position, Width = b.Width / scale, Height = b.Height / scale };
                try { w.Show(); if (!Prepare(w)) { w.Close(); CloseHosts(); _refused = true; Serilog.Log.Warning("Brain Drain: compositor blur or click-through unavailable"); return; } Hosts.Add(w); }
                catch { w.Close(); CloseHosts(); _refused = true; return; }
            }
        }
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
    private static void CloseHosts()
    {
        foreach (var w in Hosts) w.Close();
        Hosts.Clear(); _clear = null;
    }
    internal static void CloseAll()
    {
        Timer.Stop(); _refused = false;
        if (_settings != null) _settings.PropertyChanged -= Changed;
        if (_service != null) _service.CurrentReplaced -= SettingsReplaced;
        if (_owner != null) _owner.Closed -= OwnerClosed;
        _settings = null; _service = null; _owner = null;
        CloseHosts();
    }
}
