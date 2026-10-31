using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using ConditioningControlPanel.Services;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using IFrameSource = ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Avalonia.Views.Overlays;   // OutlinedTextBlock
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>WPF ChaosDvdOverlay.cs:98-107,204-239: shared host is optional;
    /// Spanker logos always use an input-enabled per-logo window. One frame subscription drives all.
    /// Remaining: the run-host rabbit/freeze producers.</summary>
    internal static class ChaosDvdOverlay
    {
        internal static readonly ChaosDvdFlock Flock = new();
        /// <summary>WPF needs no check; Linux refuses without a compositor (no per-pixel alpha, the
        /// logo would draw on a black slab). Tests swap both seams: headless has no X server.</summary>
        internal static Func<bool> CanOverlay = () => X11Overlay.IsAvailable && X11Overlay.IsCompositing;
        internal static Func<Window, PixelRect, bool> PrepareWindow = (w, bounds) =>
            X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds, passive: true, topmost: w.Topmost);
        /// <summary>Tests swap in a stepped source; live: the host window's own frame source.</summary>
        internal static Func<TopLevel, IFrameSource> FrameSourceFor = top => TopLevelFrameSource.For(top);
        internal static Action<Window> RaiseWindow = w => X11Overlay.Raise(w);
        internal static Action<string, float> PlaySfx = (k, v) => ChaosOverlayWindow.ChaosSfx.Play(k, v);

        internal static Func<bool> SharedHost = () => CoreSettings.Current.ChaosDvdSharedHost;
        internal static Func<bool> SpankerRedirect = () => ChaosRunHost.State is { SpankerActive: true } s
            && s.MaxedBoons.Contains("the_spanker"); // WPF ChaosModeService.cs:407-408
        // WPF ChaosDvdOverlay.cs:164,473-484 and ChaosWindowZ.cs:35,50: input never activates or forces pinning.
        internal static Func<Window, PixelRect, bool, bool, bool, bool> PrepareNativeWindow = (w, bounds, through, passive, pin) =>
            X11Overlay.SetClickThrough(w, through) && X11Overlay.SetOverrideRedirect(w, bounds, passive, pin);
        internal static Func<Window, PixelRect, bool, bool> PrepareLogoWindow = (w, bounds, clickable) =>
            PrepareNativeWindow(w, bounds, !clickable, true, w.Topmost);
        private static readonly Dictionary<ChaosDvdLogo, Window> Windows = new();
        private static readonly HashSet<ChaosDvdLogo> Clickable = new();
        internal static IReadOnlyDictionary<ChaosDvdLogo, Window> LogoWindows => Windows;
        private static bool _shared;
        private static double _scaling;
        private static Window? _clockWindow;
        private static Window? _window;
        private static Canvas? _canvas;
        private static IFrameSource? _source;
        private static TimeSpan? _last;
        private static Point _origin;
        private static readonly Dictionary<ChaosDvdLogo, OutlinedTextBlock> Labels = new();
        private static readonly Action<TimeSpan> OnFrameAction = OnFrame;

        internal static Window? Window => _window;
        public static bool AnyActive => Flock.AnyActive;
        public static bool AnyToyActive => Flock.AnyToyActive;

        /// <summary>WPF Launch. Refused (logged, no window) where no composited click-through overlay exists.</summary>
        public static void Launch(Visual host, double durationSec, double speedMult, double scale, int count = 1,
                                  string? text = null, int splitBounces = 0, bool splitOnRabbit = false)
        {
            // WPF Launch (:98) always hands off to the UI thread.
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => Launch(host, durationSec, speedMult, scale, count, text, splitBounces, splitOnRabbit));
                return;
            }
            try
            {
                if (!CanOverlay())
                {
                    Log.Debug("ChaosDvd: no composited click-through overlay on this platform; skipped");
                    return;
                }
                if (_canvas == null && !Create(host)) return;
                foreach (var l in Flock.Launch(durationSec, speedMult, scale, count, text, splitBounces, splitOnRabbit)) AddLabel(l);
                EnsureClock();
            }
            catch (Exception ex) { Log.Debug("ChaosDvdOverlay.Launch: {E}", ex.Message); }
        }

        private static bool Create(Visual host)
        {
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return false;
            var primary = screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
            var wa = primary.WorkingArea;
            var k = primary.Scaling > 0 ? primary.Scaling : 1.0;
            _shared = SharedHost();
            _scaling = k;
            _origin = new Point(wa.X / k, wa.Y / k);
            Flock.Left = 0;
            Flock.Top = 0;
            Flock.Right = wa.Width / k;
            Flock.Bottom = wa.Height / k;
            Flock.Measure = Measure;

            _canvas = new Canvas { IsHitTestVisible = false };
            if (!_shared) return true;
            var w = new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                Topmost = CoreSettings.Current.ChaosPinOnTop,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Focusable = false,
                IsHitTestVisible = false,
                Width = Flock.Right,
                Height = Flock.Bottom,
                Content = _canvas,
            };
            if (!PrepareWindow(w, wa))
            {
                Log.Warning("ChaosDvd: the platform refused a click-through topmost overlay window; skipped");
                w.Close();
                _canvas = null;
                return false;
            }
            w.Show();
            _window = w;
            return true;
        }

        private static (double, double) Measure(string text, double size)
        {
            var probe = Label(text, size);
            return (probe.Width, probe.Height);
        }

        /// <summary>WPF: OutlinedText, stroke #0B0812 at 2.6.</summary>
        private static OutlinedTextBlock Label(string text, double size)
        {
            var t = new OutlinedTextBlock { Text = text, FontSize = size, Family = new FontFamily("Segoe UI"),
                                            StrokeThickness = 2.6, Stroke = new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12)),
                                            IsHitTestVisible = false };
            t.Build();
            return t;
        }

        private static void AddLabel(ChaosDvdLogo l)
        {
            if (_canvas == null) return;
            var t = Label(l.Text, ChaosDvdFlock.BaseFont * l.FontScale);
            Recolor(t, l);
            t.Opacity = l.Opacity;
            Canvas.SetLeft(t, l.X);
            Canvas.SetTop(t, l.Y);
            bool clickable = SpankerRedirect(); // WPF samples the gate per launch, not per click.
            if (_shared && !clickable) _canvas.Children.Add(t);
            else
            {
                var panel = new Border { Child = t, Background = clickable
                    ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent };
                if (clickable) panel.Cursor = new Cursor(StandardCursorType.Hand);
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Background = Brushes.Transparent, Topmost = CoreSettings.Current.ChaosPinOnTop, ShowInTaskbar = false,
                    ShowActivated = false, CanResize = false, Focusable = false,
                    IsHitTestVisible = clickable, Content = panel,
                };
                var bounds = new PixelRect((int)Math.Round((_origin.X + l.X) * _scaling),
                    (int)Math.Round((_origin.Y + l.Y) * _scaling),
                    (int)Math.Ceiling(l.W * _scaling), (int)Math.Ceiling(l.H * _scaling));
                if (!PrepareLogoWindow(w, bounds, clickable)) { w.Close(); Flock.Discard(l); return; }
                w.Position = bounds.Position;
                w.Width = l.W; w.Height = l.H;
                t.IsHitTestVisible = clickable;
                if (clickable)
                {
                    Clickable.Add(l);
                    panel.PointerPressed += (_, e) =>
                    {
                        if (e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed && Smack(l)) e.Handled = true;
                    };
                }
                Windows[l] = w;
                w.Show();
            }
            Labels[l] = t;
        }

        private static void Recolor(OutlinedTextBlock t, ChaosDvdLogo l)
        {
            var (r, g, b) = ChaosDvdFlock.Hues[l.HueIndex];
            t.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
            t.InvalidateVisual();
        }

        /// <summary>WPF StepAll: baseline on the first frame, skip duplicates, clamp a stall at 0.1 s.</summary>
        private static void OnFrame(TimeSpan now)
        {
            if (_last is not { } last) { _last = now; return; }
            var dt = (now - last).TotalSeconds;
            _last = now;
            if (dt <= 0) return;
            Advance(Math.Min(dt, 0.1));
        }

        internal static void Advance(double dt)
        {
            var f = Flock.Step(dt);
            foreach (var l in f.Spawned) AddLabel(l);
            foreach (var l in f.Recolored) if (Labels.TryGetValue(l, out var t)) Recolor(t, l);
            foreach (var l in f.Removed)
            {
                if (Labels.Remove(l, out var t)) _canvas?.Children.Remove(t);
                Clickable.Remove(l);
                if (Windows.Remove(l, out var w)) w.Close();
            }
            var clones = new List<ChaosDvdLogo>();
            foreach (var (l, t) in Labels)
            {
                if (Windows.TryGetValue(l, out var w))
                    w.Position = new PixelPoint((int)Math.Round((_origin.X + l.X) * _scaling),
                        (int)Math.Round((_origin.Y + l.Y) * _scaling));
                else { Canvas.SetLeft(t, l.X); Canvas.SetTop(t, l.Y); }
                t.Opacity = l.Opacity;
                // WPF ChaosDvdOverlay.cs:388-390: collide after moving, never while retiring.
                if (l.FadeOutSec < 0 && !f.Spawned.Contains(l))
                {
                    var rect = new Rect(_origin.X + l.X, _origin.Y + l.Y, l.W, l.H);
                    BubbleOverlay.PopChaosInRect(rect);
                    if (BubbleOverlay.AnyChaosDarterIntersects(rect) && Flock.TouchRabbit(l) is { } clone)
                        clones.Add(clone);
                }
            }
            foreach (var clone in clones) { AddLabel(clone); PlaySfx("dvd_bounce", 0.4f); }
            if (f.BounceCue) PlaySfx("dvd_bounce", 0.35f);
            if (f.LaunchCue) PlaySfx("dvd_launch", 0.35f);
            if (!Flock.AnyActive) StopLoop();
            else EnsureClock();
        }

        // WPF ChaosDvdOverlay.cs:305-319: left-button input only, hue hop and unthrottled cue.
        internal static bool Smack(ChaosDvdLogo l)
        {
            if (!Clickable.Contains(l) || !Flock.Redirect(l)) return false;
            if (Labels.TryGetValue(l, out var t)) Recolor(t, l);
            PlaySfx("dvd_bounce", 0.4f);
            return true;
        }

        private static void EnsureClock()
        {
            var next = _window ?? Windows.Values.FirstOrDefault();
            if (next == _clockWindow && _source != null) return;
            StopLoop();
            if (next == null) return;
            _clockWindow = next;
            _last = null;
            _source = FrameSourceFor(next);
            _source.Subscribe(OnFrameAction);
        }

        // WPF ChaosDvdOverlay.cs:128-132, ChaosModeService.cs:745-757.
        internal static void RaiseActive()
        {
            if (!ChaosRunHost.IsDescending || !CoreSettings.Current.ChaosPinOnTop) return;
            if (_window is { IsVisible: true }) RaiseWindow(_window);
            foreach (var w in Windows.Values) if (w.IsVisible) RaiseWindow(w);
        }

        private static void StopLoop()
        {
            _source?.Unsubscribe(OnFrameAction);
            _source = null;
            _clockWindow = null;
        }

        /// <summary>WPF CloseActive (run teardown, panic): every logo and the host window go now. Idempotent.</summary>
        public static void CloseActive()
        {
            // WPF CloseActive (:131) hands off too; panic can arrive off the UI thread (P29: Post, idempotent).
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(CloseActive); return; }
            StopLoop();
            Flock.Clear();
            Labels.Clear();
            Clickable.Clear();
            foreach (var logoWindow in Windows.Values) logoWindow.Close();
            Windows.Clear();
            var w = _window;
            _window = null;
            _canvas = null;
            try { w?.Close(); } catch (Exception ex) { Log.Debug("ChaosDvd close: {E}", ex.Message); }
        }
    }
}
