using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    /// <summary>
    /// PORTED from ConditioningControlPanel/Chaos/ChaosDvdOverlay.cs and ChaosDvdHostOverlay.cs: the
    /// Porn DVD toy / Intrusive Thoughts logos. ChaosDvdSharedHost is latched until teardown:
    /// shared mode uses a stage canvas; default mode pools one click-through window per logo.
    /// Motion remains in the primary work area. Core ChaosDvdFlock owns motion and fades.
    /// The frame subscription drops when the last logo retires; windows live until CloseActive.
    /// <para>Missing: Spanker input and re-stacking above mandatory video. Rabbit splitting
    /// awaits the live darter lifecycle; the thought freeze gate awaits the freeze pickup.</para>
    /// </summary>
    internal static class ChaosDvdOverlay
    {
        internal static readonly ChaosDvdFlock Flock = new();
        /// <summary>WPF needs no check; Linux refuses without a compositor (no per-pixel alpha, the
        /// logo would draw on a black slab). Tests swap both seams: headless has no X server.</summary>
        internal static Func<bool> CanOverlay = () => X11Overlay.IsAvailable && X11Overlay.IsCompositing;
        internal static Func<Window, PixelRect, bool> PrepareWindow = (w, bounds) =>
            X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, bounds);
        /// <summary>Tests swap in a stepped source; live: the host window's own frame source.</summary>
        internal static Func<TopLevel, IFrameSource> FrameSourceFor = top => TopLevelFrameSource.For(top);
        internal static Action<string, float> PlaySfx = (k, v) => ChaosOverlayWindow.ChaosSfx.Play(k, v);

        private static bool _initialized, _sharedHost;
        private static PixelRect _stage;
        private static double _scale;
        private static readonly Dictionary<ChaosDvdLogo, Window> LogoWindows = new();
        private static readonly Stack<Window> Pool = new();
        private static Window? _window;
        private static Canvas? _canvas;
        private static IFrameSource? _source;
        private static Window? _frameWindow;
        private static TimeSpan? _last;
        private static readonly Dictionary<ChaosDvdLogo, OutlinedTextBlock> Labels = new();
        private static readonly Action<TimeSpan> OnFrameAction = OnFrame;

        internal static Window? Window => _window ?? LogoWindows.Values.FirstOrDefault() ?? Pool.FirstOrDefault();
        internal static IReadOnlyList<Window> Windows => _window != null ? new[] { _window } : LogoWindows.Values.Concat(Pool).ToArray();
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
                if (!_initialized && !Create(host)) return;
                foreach (var l in Flock.Launch(durationSec, speedMult, scale, count, text, splitBounces, splitOnRabbit)) AddLabel(l);
                EnsureLoop();
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
            // WPF ChaosDvdHostOverlay :122-124 + ChaosWindowZ :96-104: stage spans the
            // configured monitors. WPF ChaosDvdOverlay :216-228 still bounces in primary work area.
            var stage = primary.Bounds;
            if (ConditioningControlPanel.CoreSettings.Current.DualMonitorEnabled)
                foreach (var screen in screens) stage = stage.Union(screen.Bounds);
            Flock.Left = (wa.X - stage.X) / k;
            Flock.Top = (wa.Y - stage.Y) / k;
            Flock.Right = (wa.Right - stage.X) / k;
            Flock.Bottom = (wa.Bottom - stage.Y) / k;
            Flock.Measure = Measure;
            // WPF ChaosDvdOverlay.cs:390, :394: stage-relative DIPs to global DIPs.
            Flock.Contact = l => BubbleOverlay.ContactChaos(ContactRect(l));
            Flock.RabbitContact = l => BubbleOverlay.RabbitContact(ContactRect(l));

            _stage = stage;
            _scale = k;
            // WPF ChaosDvdOverlay :101-103: latch once, including across empty intervals.
            _sharedHost = ConditioningControlPanel.CoreSettings.Current.ChaosDvdSharedHost;
            _initialized = true;
            if (!_sharedHost) return true;
            _canvas = new Canvas { IsHitTestVisible = false };
            var w = NewWindow(_canvas, stage.Width / k, stage.Height / k);
            if (!PrepareWindow(w, stage))
            {
                Log.Warning("ChaosDvd: the platform refused a click-through topmost overlay window; skipped");
                w.Close();
                _canvas = null;
                _initialized = false;
                return false;
            }
            w.Show();
            _window = w;
            return true;
        }

        private static Window NewWindow(Control content, double width, double height) => new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Focusable = false,
                IsHitTestVisible = false,
                Width = width,
                Height = height,
                Content = content,
            };

        private static Rect ContactRect(ChaosDvdLogo l) => new(
            l.X + _stage.X / _scale, l.Y + _stage.Y / _scale, l.W, l.H);

        private static PixelRect LogoBounds(ChaosDvdLogo l) => new(
            _stage.X + (int)Math.Round(l.X * _scale), _stage.Y + (int)Math.Round(l.Y * _scale),
            Math.Max(1, (int)Math.Ceiling(l.W * _scale)), Math.Max(1, (int)Math.Ceiling(l.H * _scale)));

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
            var t = Label(l.Text, ChaosDvdFlock.BaseFont * l.FontScale);
            Recolor(t, l);
            t.Opacity = l.Opacity;
            Canvas.SetLeft(t, l.X);
            Canvas.SetTop(t, l.Y);
            if (_sharedHost) _canvas!.Children.Add(t);
            else
            {
                // WPF ChaosDvdOverlay :115-122, :244-255, :435-448: reuse hidden windows.
                var w = Pool.Count > 0 ? Pool.Pop() : NewWindow(t, l.W, l.H);
                w.Width = l.W;
                w.Height = l.H;
                w.Content = t;
                if (!PrepareWindow(w, LogoBounds(l)))
                {
                    Pool.Push(w);
                    CloseActive(); // refusal must never leave an input-blocking overlay
                    throw new InvalidOperationException("DVD click-through window refused");
                }
                w.Show();
                LogoWindows[l] = w;
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
                if (Labels.Remove(l, out var t))
                {
                    _canvas?.Children.Remove(t);
                    if (LogoWindows.Remove(l, out var w)) { w.Hide(); Pool.Push(w); }
                }
            foreach (var (l, t) in Labels)
            {
                Canvas.SetLeft(t, l.X);
                Canvas.SetTop(t, l.Y);
                t.Opacity = l.Opacity;
                if (LogoWindows.TryGetValue(l, out var w)) w.Position = LogoBounds(l).Position;
            }
            if (f.BounceCue) PlaySfx("dvd_bounce", 0.35f);
            for (int i = 0; i < f.RabbitCues; i++) PlaySfx("dvd_bounce", 0.4f);
            if (f.LaunchCue) PlaySfx("dvd_launch", 0.35f);
            if (!Flock.AnyActive) StopLoop();
            else EnsureLoop(); // a per-logo driver may have just retired and hidden
        }

        private static void EnsureLoop()
        {
            var next = Window;
            if (next == null || ReferenceEquals(next, _frameWindow)) return;
            StopLoop();
            _frameWindow = next;
            _last = null;
            _source = FrameSourceFor(next);
            _source.Subscribe(OnFrameAction);
        }

        private static void StopLoop()
        {
            _source?.Unsubscribe(OnFrameAction);
            _source = null;
            _frameWindow = null;
        }

        /// <summary>WPF CloseActive (run teardown, panic): every logo and the host window go now. Idempotent.</summary>
        public static void CloseActive()
        {
            // WPF CloseActive (:131) hands off too; panic can arrive off the UI thread (P29: Post, idempotent).
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(CloseActive); return; }
            StopLoop();
            Flock.Clear();
            Flock.Contact = null;
            Flock.RabbitContact = null;
            Labels.Clear();
            foreach (var logoWindow in LogoWindows.Values.Concat(Pool))
                try { logoWindow.Close(); } catch (Exception ex) { Log.Debug("ChaosDvd close: {E}", ex.Message); }
            LogoWindows.Clear();
            Pool.Clear();
            _initialized = false;
            var w = _window;
            _window = null;
            _canvas = null;
            try { w?.Close(); } catch (Exception ex) { Log.Debug("ChaosDvd close: {E}", ex.Message); }
        }
    }
}
