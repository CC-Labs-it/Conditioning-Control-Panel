// PORTED from ConditioningControlPanel/Controls/ChasterBookedPop.cs: the big "+3:00" that pops
// where the time was earned (the popped bubble, the flash that showed), else at the cursor. Its own
// small transparent, topmost, click-through window, never activated (WPF :21-28, here X11Overlay's
// click-through + override-redirect, as BouncingTextOverlay). Layout and timing are Core
// BookedPopLayout; colour and text Core BookedFlashPlan. Where the platform refuses such a window
// (Wayland, headless) Show returns null and the shell takes WPF's rail-flash fallback.
// Motion runs on one FrameClock that stops when the pop's life ends (P82: no Animation on a
// transform, no live Effect: WPF's DropShadowEffect glow is a wide coloured stroke under the outline).
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    internal sealed class ChasterBookedPop : Window
    {
        internal static readonly List<ChasterBookedPop> Live = new();

        /// <summary>Seam: make the unshown window click-through and topmost at <c>bounds</c> (physical px). False = this platform cannot.</summary>
        internal static Func<Window, PixelRect, bool> Place = (w, b) =>
            X11Overlay.IsAvailable && X11Overlay.IsCompositing && X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, b);
        /// <summary>Seam: the cursor in physical desktop px (WPF GetCursorPos on Windows, XQueryPointer on X11).</summary>
        internal static Func<ScreenPoint?> Cursor = () => OperatingSystem.IsWindows()
            ? Win32Overlay.CursorPosition() is { } p ? new ScreenPoint(p.X, p.Y) : null
            : X11Pointer.Read() is { } r ? new ScreenPoint(r.At.X, r.At.Y) : null;
        internal static Func<TopLevel, bool> Raise = X11Overlay.Raise;
        internal static TimeProvider Time = TimeProvider.System;
        internal static ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource? Frames;

        private static readonly Random Rng = new();

        internal BookedPopLayout.PopPlan Plan { get; }
        internal ScreenPoint Origin { get; }
        internal int Slot { get; }
        internal PopStage Stage { get; }
        private readonly long _born;
        private readonly long _bornMs = Environment.TickCount64;
        private FrameClock? _clock;
        private long? _punch;
        internal bool Gone { get; private set; }

        /// <summary>--render-all only: a fresh "+3:00" mid-life over a dim plate.</summary>
        internal ChasterBookedPop() : this(default, 0, new BookedFlashPlan.Plan("+3:00", BookedFlashPlan.AddColour, 0, 1500, true, "attention"),
            BookedPopLayout.For(Models.MotionLevel.Full))
        {
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x28, 0x3C));
            Stage.At(400, null);
        }

        private ChasterBookedPop(ScreenPoint origin, int slot, BookedFlashPlan.Plan look, BookedPopLayout.PopPlan plan)
        {
            Plan = plan;
            Origin = origin;
            Slot = slot;
            _born = Time.GetTimestamp();
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            Title = "";
            Width = BookedPopLayout.WindowWidthDip;
            Height = BookedPopLayout.WindowHeightDip;
            Stage = new PopStage(plan, plan.Particles);
            Stage.Paint(look.Text, look.Colour, look.Source);
            Content = Stage;
            // WPF OnContentRendered: reassert above overlays mapped between creation and Show.
            Opened += (_, _) => Raise(this);
        }

        /// <summary>WPF Show: pop a figure at <paramref name="origin"/> (physical px), stacked above
        /// live pops near it. Null when the platform cannot show it (decoration fails quietly).</summary>
        internal static ChasterBookedPop? Show(TopLevel? host, ScreenPoint origin, BookedFlashPlan.Plan look, BookedPopLayout.PopPlan plan)
        {
            try
            {
                Live.RemoveAll(p => p.Gone);
                var now = Environment.TickCount64;
                var live = new List<BookedPopLayout.LivePop>(Live.Count);
                foreach (var p in Live) live.Add(new BookedPopLayout.LivePop(p.Origin, p._bornMs, p.Plan.TotalMs, p.Slot));
                var slot = BookedPopLayout.PickSlot(live, origin, now);

                var screen = host?.Screens?.ScreenFromPoint(new PixelPoint((int)origin.X, (int)origin.Y)) ?? host?.Screens?.Primary;
                var scale = screen?.Scaling is > 0 and var k ? k : 1.0;
                AvatarTubeLayout.Box? monitor = screen is { } s ? new(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height) : null;
                var tl = BookedPopLayout.TopLeftPx(origin, slot, scale, monitor);
                var bounds = new PixelRect((int)tl.X, (int)tl.Y,
                    (int)Math.Ceiling(BookedPopLayout.WindowWidthDip * scale), (int)Math.Ceiling(BookedPopLayout.WindowHeightDip * scale));

                var pop = new ChasterBookedPop(origin, slot, look, plan);
                if (!Place(pop, bounds)) { pop.Close(); return null; }
                Live.Add(pop);
                pop.Show();
                pop.Run();
                return pop;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("[Chaster] booked pop: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>Close every pop now (the shell is going away): an open pop must not hold the app.</summary>
        internal static void CloseAll()
        {
            foreach (var p in Live.ToArray()) p.Dismiss();
            Live.Clear();
        }

        /// <summary>Same figure, bigger number, and a small re-punch so the change is seen (WPF Retitle).</summary>
        internal void Retitle(string text, uint colour, string? source = null)
        {
            if (Gone) return;
            Stage.Paint(text, colour, source, keepSource: source is null);
            if (Plan.ScaleInMs > 0) _punch = Time.GetTimestamp();
            Tick();
        }

        internal void Dismiss()
        {
            if (Gone) return;
            Gone = true;
            _clock?.Stop();
            Live.Remove(this);
            try { Close(); }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] booked pop close: {E}", ex.Message); }
        }

        private void Run()
        {
            _clock = Frames is { } f ? new FrameClock(f) : new FrameClock(this);
            _clock.Interval = TimeSpan.FromMilliseconds(16);
            _clock.Tick += (_, _) => Tick();
            _clock.Start();
            Tick();
        }

        /// <summary>One frame from real elapsed time (P40); the end of life closes the window, so nothing ticks after.</summary>
        private void Tick()
        {
            var ms = Time.GetElapsedTime(_born).TotalMilliseconds;
            if (ms >= Math.Max(1, Plan.TotalMs)) { Dismiss(); return; }
            Stage.At(ms, _punch is { } p ? Time.GetElapsedTime(p).TotalMilliseconds : null);
        }

        /// <summary>The faded name of what cost the time, or null when the row has no copy (a raw id is never shown).</summary>
        internal static string? SourceName(string? eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            var key = TabMenuCopy.ShortKey(eventId);
            var name = Loc.Get(key);
            return string.IsNullOrWhiteSpace(name) || name == key ? null : name;
        }

        /// <summary>Scale, lift, stage opacity and glow at <c>ms</c> into the life: WPF's Run (:239-282) as one pure function.</summary>
        internal readonly record struct Frame(double Scale, double Rise, double Opacity, double Glow);

        internal static Frame At(BookedPopLayout.PopPlan p, double ms, double? punchMs)
        {
            var life = Math.Max(1, p.TotalMs);
            var scale = 1.0;
            var glow = 22.0;
            if (p.ScaleInMs > 0)
            {
                if (ms < p.ScaleInMs) scale = 0.2 + 0.8 * BackOut(ms / p.ScaleInMs, p.Overshoot);
                if (ms < p.ScaleInMs * 2) glow = 60 - 38 * (ms / (p.ScaleInMs * 2));   // the glow flares at birth and settles
                if (punchMs is { } q) scale = q < 180 ? 1.18 - 0.18 * BackOut(q / 180, 0.4) : 1.0;   // WPF holds 1.0 after the punch
            }
            var rise = p.RiseDip > 0 ? -p.RiseDip * (1 - Math.Pow(1 - Math.Min(1, ms / life), 3)) : 0;
            var opacity = 1.0;
            if (p.FadeMs > 0)
            {
                var start = life - Math.Min(p.FadeMs, life);
                if (ms > start) opacity = Math.Clamp(1 - (ms - start) / (life - start), 0, 1);
            }
            return new Frame(scale, rise, opacity, glow);
        }

        /// <summary>WPF BackEase EaseOut: 1 - f(1 - t), f(u) = u^3 - u * a * sin(pi u).</summary>
        private static double BackOut(double t, double a)
        {
            var u = 1 - Math.Clamp(t, 0, 1);
            return 1 - (u * u * u - u * Math.Max(0, a) * Math.Sin(Math.PI * u));
        }

        /// <summary>The figure, its source badge and the sparks, drawn from the current <see cref="Frame"/>.</summary>
        internal sealed class PopStage : Control
        {
            private static readonly FontFamily Display = new("Fredoka, Segoe UI");
            private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromArgb(0xF0, 0x1A, 0x0C, 0x1E));
            private static readonly IBrush BadgeBrush = new SolidColorBrush(Color.FromArgb(0xC8, 0x1A, 0x0C, 0x1E));
            private readonly BookedPopLayout.PopPlan _plan;
            private readonly (double Angle, double Dist, double Size, double Ms, bool White)[] _sparks;
            private Geometry? _geometry;
            private FormattedText? _source;
            private Color _colour;
            private double _ms;
            internal Frame Now { get; private set; } = new(1, 0, 1, 22);
            internal string Text { get; private set; } = "";
            internal string? Source { get; private set; }

            internal PopStage(BookedPopLayout.PopPlan plan, int sparks)
            {
                _plan = plan;
                IsHitTestVisible = false;
                Width = BookedPopLayout.WindowWidthDip;
                Height = BookedPopLayout.WindowHeightDip;
                _sparks = new (double, double, double, double, bool)[Math.Max(0, sparks)];
                for (var i = 0; i < _sparks.Length; i++)
                    _sparks[i] = ((i + Rng.NextDouble() * 0.6) / _sparks.Length * Math.PI * 2, 70 + Rng.NextDouble() * 60,
                        5 + Rng.NextDouble() * 5, 450 + Rng.Next(300), i % 3 == 0);
            }

            /// <summary>The figure as outlined geometry, centred on the anchor (WPF Paint :183-211) and the badge (Label :215-228).</summary>
            internal void Paint(string text, uint colour, string? eventId, bool keepSource = false)
            {
                Text = text;
                _colour = Color.FromUInt32(colour);
                var figure = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Display, FontStyle.Normal, FontWeight.Bold), _plan.FontDip, Brushes.White);
                var cx = BookedPopLayout.WindowWidthDip / 2;
                var cy = BookedPopLayout.WindowHeightDip * BookedPopLayout.FigureAnchorY;
                _geometry = figure.BuildGeometry(new Point(cx - figure.WidthIncludingTrailingWhitespace / 2, cy - figure.Height / 2));
                if (!keepSource)
                {
                    Source = SourceName(eventId);
                    _source = Source is null ? null : new FormattedText(Source, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(Display, FontStyle.Normal, FontWeight.SemiBold), 15, new SolidColorBrush(Lighten(_colour, 0.55)));
                }
                else if (_source is { } s) s.SetForegroundBrush(new SolidColorBrush(Lighten(_colour, 0.55)));
                InvalidateVisual();
            }

            internal void At(double ms, double? punchMs)
            {
                _ms = ms;
                Now = ChasterBookedPop.At(_plan, ms, punchMs);
                InvalidateVisual();
            }

            public override void Render(DrawingContext ctx)
            {
                var cx = BookedPopLayout.WindowWidthDip / 2;
                var cy = BookedPopLayout.WindowHeightDip * BookedPopLayout.FigureAnchorY;
                var f = Now;
                using var fade = ctx.PushOpacity(f.Opacity);
                foreach (var s in _sparks)
                {
                    var t = Math.Min(1, _ms / s.Ms);
                    if (t >= 1) continue;
                    var q = 1 - (1 - t) * (1 - t);
                    var at = new Point(cx + Math.Cos(s.Angle) * s.Dist * q, cy + Math.Sin(s.Angle) * s.Dist * 0.8 * q);
                    using (ctx.PushOpacity(1 - t))
                        ctx.DrawEllipse(s.White ? Brushes.White : new SolidColorBrush(_colour), null, at, s.Size / 2, s.Size / 2);
                }
                if (_geometry is { } g)
                {
                    var m = Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateScale(f.Scale, f.Scale) * Matrix.CreateTranslation(cx, cy + f.Rise);
                    using (ctx.PushTransform(m))
                    {
                        var outline = Math.Max(8, _plan.FontDip * 0.2);
                        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(_colour, 0.35), outline + f.Glow * 0.6, lineJoin: PenLineJoin.Round), g);
                        ctx.DrawGeometry(null, new Pen(OutlineBrush, outline, lineJoin: PenLineJoin.Round), g);
                        ctx.DrawGeometry(new LinearGradientBrush
                        {
                            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                            GradientStops = { new GradientStop(Lighten(_colour, 0.35), 0), new GradientStop(_colour, 1) },
                        }, null, g);
                    }
                    if (_source is { } src)
                    {
                        // Rides the rise but not the scale punch, so it reads as a caption.
                        var w = src.WidthIncludingTrailingWhitespace + 18;
                        var h = src.Height + 5;
                        var top = Math.Min(g.Bounds.Bottom + 10, BookedPopLayout.WindowHeightDip - h - 4) + f.Rise;
                        using (ctx.PushOpacity(BookedFlashPlan.SourceOpacity))
                        {
                            ctx.DrawRectangle(BadgeBrush, null, new Rect(cx - w / 2, top, w, h), 9, 9);
                            ctx.DrawText(src, new Point(cx - w / 2 + 9, top + 2));
                        }
                    }
                }
            }

            private static Color Lighten(Color c, double t) => Color.FromRgb(
                (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
        }
    }
}
