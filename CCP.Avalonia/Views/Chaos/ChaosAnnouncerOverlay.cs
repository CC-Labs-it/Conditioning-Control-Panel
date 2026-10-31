using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Chaos/ChaosAnnouncerOverlay.cs: a full-screen, click-through
    /// overlay that flashes a bordered "subtitle" line just under the effect-banner strip to announce a
    /// Chaos beat. Announcements queue (<see cref="ChaosAnnounceQueue"/>, Core) so back-to-back beats play
    /// in sequence; the envelope is <see cref="ChaosAnnounceTiming"/> (Core). Master-gated on
    /// <c>ChaosAnnouncerEnabled</c>. ONE window is created on first use and kept alive (hidden while idle),
    /// as WPF does; <see cref="CloseActive"/> drops the queue and closes it (run teardown and panic).
    /// <para>ponytail: not here - the neon word-art banners (WPF <c>ChaosArt.Resolve("announce", key)</c>;
    /// this head has no ChaosArt, so every line takes WPF's missing-art path: the outlined text), the
    /// narrator line (<c>AnnounceNarrator</c> + CutShort; no ChaosNarrator on this head - the priority
    /// rule itself is in the Core queue) and <c>RaiseActive</c> over a mandatory video.</para>
    /// </summary>
    internal sealed class ChaosAnnouncerOverlay : Window
    {
        private const double FontSizeDip = 60;
        private const double StrokeDip = 3.2;
        private const double TopOffsetDip = 92;   // right under the effect-banner strip (wa.Top+6, 80 tall)

        private static readonly ChaosAnnounceQueue Queue = new();
        private static ChaosAnnouncerOverlay? _active;
        private static bool _showing;

        /// <summary>Test seam (P08): elapsed time of the envelope.</summary>
        internal static TimeProvider Clock { get; set; } = TimeProvider.System;

        internal static ChaosAnnouncerOverlay? Active => _active;
        internal static bool IsShowing => _showing;
        internal static int QueuedCount => Queue.Count;

        private readonly Grid _host = new() { IsHitTestVisible = false };
        private readonly BeatLoop? _frame;   // the shared 30 fps beat (no per-view frame timer)
        private ChaosAnnouncement? _current;
        private long _startTs;
        private ScaleTransform? _scale;

        /// <summary>The line on screen now (null while idle).</summary>
        internal ChaosAnnouncement? Current => _current;
        internal double ContentOpacity => _host.Opacity;
        internal double ContentScale => _scale?.ScaleX ?? 1;

        /// <summary>WPF Announce: queue a bordered fading line. No-op unless the announcer is enabled.</summary>
        public static void Announce(string text, ChaosAnnounceKind kind,
                                    string? artKey = null, string? subText = null, int? holdMs = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                if (CoreSettings.Current?.ChaosAnnouncerEnabled != true) return;
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => Announce(text, kind, artKey, subText, holdMs));
                    return;
                }
                Queue.Add(new ChaosAnnouncement(text, kind, artKey, subText, holdMs ?? ChaosAnnounceTiming.HoldMs, 0));
                if (!_showing) ShowNext();
            }
            catch (Exception ex) { Log.Debug("ChaosAnnouncer.Announce: {E}", ex.Message); }
        }

        /// <summary>Drop any queued/visible announcement and tear the window down (run teardown, panic).
        /// Safe when idle.</summary>
        public static void CloseActive()
        {
            Queue.Clear();
            _showing = false;
            var w = _active;
            _active = null;
            try { w?.CloseNow(); } catch (Exception ex) { Log.Debug("ChaosAnnouncer.CloseActive: {E}", ex.Message); }
        }

        private static void ShowNext()
        {
            if (!Queue.TryDequeue(out var next)) { _showing = false; return; }
            _showing = true;
            try
            {
                if (_active == null) { _active = new ChaosAnnouncerOverlay(live: true); _active.Show(); }
                else if (!_active.IsVisible) _active.Show();   // idles hidden between announcements
                _active.Display(next);
            }
            catch (Exception ex)
            {
                Log.Debug("ChaosAnnouncer.ShowNext: {E}", ex.Message);
                _showing = false;
            }
        }

        /// <summary>--render-all only: one line at full opacity, never queued, never timed.</summary>
        internal ChaosAnnouncerOverlay() : this(live: false)
        {
            Width = 1280;
            Height = 720;
            Build(new ChaosAnnouncement("the hole is closing…", ChaosAnnounceKind.Depth, "ending_soon", "ten seconds",
                ChaosAnnounceTiming.HoldMs, 0), (0, 0, double.NaN));
            _host.Opacity = 1;
            _scale!.ScaleX = _scale.ScaleY = 1;
        }

        private ChaosAnnouncerOverlay(bool live)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.Current?.ChaosPinOnTop ?? true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            Title = "CCP Chaos announcer";
            _host.Opacity = 0;
            Content = _host;
            if (!live) return;

            Opened += (_, _) =>
            {
                SizeToPrimaryScreen();
                X11Overlay.SetClickThrough(this, true);   // WS_EX_TRANSPARENT twin; false (no-op) headless
            };
            _frame = new BeatLoop(_host, _ => Step());
            Closed += (_, _) =>
            {
                _frame.Stop();
                if (ReferenceEquals(_active, this)) { _active = null; Queue.Clear(); _showing = false; }
            };
        }

        /// <summary>WPF DisplayCore: swap the label, restart the envelope.</summary>
        private void Display(ChaosAnnouncement a)
        {
            Build(a, WorkAreaAnchor());
            _current = a;
            _startTs = Clock.GetTimestamp();
            Step();
            _frame?.Start();
        }

        /// <summary>One frame of the envelope; the timer calls it, tests call it after stepping <see cref="Clock"/>.
        /// At the end WPF's fade.Completed runs: clear content, hide when nothing is queued, show the next.</summary>
        internal void Step()
        {
            if (_current is not { } a) return;
            var ms = Clock.GetElapsedTime(_startTs).TotalMilliseconds;
            _host.Opacity = ChaosAnnounceTiming.Opacity(ms, a.HoldMs);
            if (_scale != null) _scale.ScaleX = _scale.ScaleY = ChaosAnnounceTiming.Scale(ms);
            if (!ChaosAnnounceTiming.IsDone(ms, a.HoldMs)) return;

            _frame?.Stop();
            _current = null;
            _host.Children.Clear();   // window stays - only content goes
            if (Queue.Count == 0) { try { Hide(); } catch (Exception ex) { Log.Debug("ChaosAnnouncer hide: {E}", ex.Message); } }
            ShowNext();
        }

        private void Build(ChaosAnnouncement a, (double left, double top, double width) wa)
        {
            var (fill, stroke) = Palette(a.Kind);
            var label = new OutlinedTextBlock
            {
                Text = a.Text.ToUpperInvariant(),
                FontSize = FontSizeDip,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = StrokeDip,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            };
            label.Build();
            Control line = label;
            // WPF ChaosModeService.cs:434 prefixes a mantra with a diamond. Keep the mark
            // in the head as a Fluent icon, outside the outlined announcement text.
            if (a.Kind == ChaosAnnounceKind.Mantra)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
                row.Children.Add(new IconGlyph { Kind = IconKind.Diamond, Variant = IconVariant.Filled,
                    Size = FontSizeDip, Foreground = fill, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0) });
                row.Children.Add(label);
                line = row;
            }
            _scale = new ScaleTransform(ChaosAnnounceTiming.PopFrom, ChaosAnnounceTiming.PopFrom);
            line.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            line.RenderTransform = _scale;

            // Anchored just under the effect-banner strip, centred over the primary work area.
            var anchor = new Grid
            {
                HorizontalAlignment = double.IsNaN(wa.width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(wa.left, wa.top + TopOffsetDip, 0, 0),
                IsHitTestVisible = false,
            };
            if (!double.IsNaN(wa.width)) anchor.Width = wa.width;
            anchor.Children.Add(line);
            _host.Children.Clear();
            _host.Children.Add(anchor);
        }

        /// <summary>WPF read SystemParameters.WorkArea; the twin is Screens.Primary (physical pixels, hence
        /// the scaling). This window covers the primary screen, so the anchor is relative to its bounds.</summary>
        private (double left, double top, double width) WorkAreaAnchor()
        {
            try
            {
                var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
                if (screen is null) return (0, 0, double.NaN);
                return WorkAreaAnchor(screen.Bounds, screen.WorkingArea, screen.Scaling);
            }
            catch { return (0, 0, double.NaN); }
        }

        /// <summary>WPF adds wa.Left too (ChaosAnnouncerOverlay.cs:244), so a left-docked panel keeps
        /// the line centred on the work area, not the screen.</summary>
        internal static (double left, double top, double width) WorkAreaAnchor(PixelRect bounds, PixelRect workingArea, double scaling)
            => (Math.Max(0, (workingArea.X - bounds.X) / scaling),
                Math.Max(0, (workingArea.Y - bounds.Y) / scaling),
                workingArea.Width / scaling);

        private void SizeToPrimaryScreen()
        {
            try
            {
                var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
                if (screen is null) return;
                Position = screen.Bounds.Position;
                Width = screen.Bounds.Width / screen.Scaling;
                Height = screen.Bounds.Height / screen.Scaling;
            }
            catch (Exception ex) { Log.Debug("ChaosAnnouncer: no screen to size to ({E})", ex.Message); }
        }

        private void CloseNow()
        {
            _frame?.Stop();
            _current = null;
            _host.Children.Clear();
            Close();
        }

        /// <summary>WPF Palette: accent fill per kind, near-black stroke for contrast.</summary>
        internal static (IBrush fill, IBrush stroke) Palette(ChaosAnnounceKind kind)
        {
            Color c = kind switch
            {
                ChaosAnnounceKind.Mantra     => Color.FromRgb(0xFF, 0xD2, 0x7A), // warm gold
                ChaosAnnounceKind.Temptation => Color.FromRgb(0xFF, 0x6B, 0x6B), // risky red
                ChaosAnnounceKind.Willpower  => Color.FromRgb(0x7A, 0xE0, 0xFF), // cyan
                ChaosAnnounceKind.Depth      => Color.FromRgb(0xFF, 0xFF, 0xFF), // white
                ChaosAnnounceKind.Streak     => Color.FromRgb(0xFF, 0xC8, 0x3C), // bright gold
                ChaosAnnounceKind.Item       => Color.FromRgb(0x7A, 0xFF, 0xD2), // mint
                ChaosAnnounceKind.PowerUp    => Color.FromRgb(0x9C, 0xE8, 0xA0), // green
                ChaosAnnounceKind.Narrator   => Color.FromRgb(0xE6, 0x9A, 0xFF), // the Madam - soft violet
                _                            => Colors.White,
            };
            return (new SolidColorBrush(c), new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12)));
        }
    }
}
