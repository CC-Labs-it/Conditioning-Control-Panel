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
    /// Porn DVD toy / Intrusive Thoughts logos. WPF's default is one layered window per logo, with the
    /// experimental shared host (ChaosDvdSharedHost) as one full-stage click-through canvas; this head
    /// always uses the host shape - one click-through override-redirect window over the primary work
    /// area (WPF bounces in SystemParameters.WorkArea), every logo an <see cref="OutlinedTextBlock"/>
    /// on its canvas. Motion is Core <see cref="ChaosDvdFlock"/>, stepped from the window's shared
    /// <see cref="TopLevelFrameSource"/> (WPF CompositionTarget.Rendering, dt clamped at 0.1 s); the
    /// subscription drops when the last logo retires. The window lives until <see cref="CloseActive"/>.
    ///
    /// <para>The Porn DVD toy is launched by ChaosRunHost. Part B: Intrusive Thoughts,
    /// Spanker smack, rabbit split, per-logo hosts and RaiseActive above video.</para>
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
                                  string? text = null, int splitBounces = 0)
        {
            // WPF Launch (:98) always hands off to the UI thread.
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => Launch(host, durationSec, speedMult, scale, count, text, splitBounces));
                return;
            }
            try
            {
                if (!CanOverlay())
                {
                    Log.Debug("ChaosDvd: no composited click-through overlay on this platform; skipped");
                    return;
                }
                if (_window == null && !Create(host)) return;
                foreach (var l in Flock.Launch(durationSec, speedMult, scale, count, text, splitBounces)) AddLabel(l);
                if (_source == null && _window != null)
                {
                    _last = null;
                    _source = FrameSourceFor(_window);
                    _source.Subscribe(OnFrameAction);
                }
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
            _origin = new Point(wa.X / k, wa.Y / k);
            Flock.Left = 0;
            Flock.Top = 0;
            Flock.Right = wa.Width / k;
            Flock.Bottom = wa.Height / k;
            Flock.Measure = Measure;

            _canvas = new Canvas { IsHitTestVisible = false };
            var w = new Window
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
            _canvas.Children.Add(t);
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
                if (Labels.Remove(l, out var t)) _canvas?.Children.Remove(t);
            foreach (var (l, t) in Labels)
            {
                Canvas.SetLeft(t, l.X);
                Canvas.SetTop(t, l.Y);
                t.Opacity = l.Opacity;
                // WPF ChaosDvdOverlay.cs:388-390: collide after moving, never while retiring.
                if (l.FadeOutSec < 0 && !f.Spawned.Contains(l))
                    BubbleOverlay.PopChaosInRect(new Rect(_origin.X + l.X, _origin.Y + l.Y, l.W, l.H));
            }
            if (f.BounceCue) PlaySfx("dvd_bounce", 0.35f);
            if (f.LaunchCue) PlaySfx("dvd_launch", 0.35f);
            if (!Flock.AnyActive) StopLoop();
        }

        private static void StopLoop()
        {
            _source?.Unsubscribe(OnFrameAction);
            _source = null;
        }

        /// <summary>WPF CloseActive (run teardown, panic): every logo and the host window go now. Idempotent.</summary>
        public static void CloseActive()
        {
            // WPF CloseActive (:131) hands off too; panic can arrive off the UI thread (P29: Post, idempotent).
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(CloseActive); return; }
            StopLoop();
            Flock.Clear();
            Labels.Clear();
            var w = _window;
            _window = null;
            _canvas = null;
            try { w?.Close(); } catch (Exception ex) { Log.Debug("ChaosDvd close: {E}", ex.Message); }
        }
    }
}
