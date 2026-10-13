using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// WPF <c>ConditioningControlPanel/Overlays/SpeakCueOverlay.cs</c>: a virtual-screen, topmost,
    /// click-through window holding one voice-prompt cue ("SAY IT" over e.g. "Say YES") 120 DIP below the
    /// top of the primary work area, faded in over 180 ms to 0.92 and held with NO timer until
    /// <see cref="HideCue"/>. Rules (blank text, fade, anchor, show-during-fade-out) live in Core
    /// <see cref="SpeakCueState"/>.
    ///
    /// <para>Win32 styles map as in <see cref="TintOverlayWindow"/>; the fade is compositor alpha
    /// (<see cref="X11Overlay.SetOpacity"/>), as SubliminalOverlay's, so a fade frame costs no re-render.
    /// Differs from WPF on purpose: WPF keeps the window hidden between cues (its layered windows churn
    /// the render thread); here it closes after the fade out, so no hidden window can keep the process
    /// alive under OnLastWindowClose.</para>
    ///
    /// <para>ponytail: no caller yet. WPF's only caller is Deeper's SpeakPromptSession (Persistent cue
    /// mode, SpeakPromptSession.cs:172/196), which needs the Deeper runtime ported (deeper-core-c/d).</para>
    /// </summary>
    internal sealed class SpeakCueOverlay : Window
    {
        private static readonly IBrush CueFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly IBrush StrokeBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x06, 0x1A));
        private static readonly IBrush AccentFill = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0xD8));

        private static SpeakCueOverlay? _active;
        /// <summary>Warn once per process; internal so tests restore it and never leak it into each other.</summary>
        internal static bool Warned;

        /// <summary>The cue window on screen (or fading out), if any.</summary>
        internal static SpeakCueOverlay? Active => _active;

        /// <summary>Test seams: the fade clock, the platform setup before Show (false = refused), the alpha sink.</summary>
        internal static TimeProvider Clock = TimeProvider.System;
        internal static Func<Window, PixelRect, bool> Prepare = (w, bounds) =>
            X11Overlay.IsAvailable && X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOpacity(w, 0)
            && X11Overlay.SetOverrideRedirect(w, bounds);
        internal static Action<Window, double> ApplyAlpha = (w, a) => X11Overlay.SetOpacity(w, a);

        /// <summary>Show (or replace) the persistent cue. Safe from any thread.</summary>
        public static void ShowCue(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ShowCue(text)); return; }
            try
            {
                if (_active == null)
                {
                    var w = new SpeakCueOverlay(live: true);
                    if (!Prepare(w, w.VirtualScreen()))
                    {
                        if (!Warned) Log.Warning("SpeakCueOverlay: this platform cannot show a click-through topmost overlay; cue skipped");
                        Warned = true;
                        w.Close();
                        return;
                    }
                    _active = w;
                    w.Show();
                }
                _active.Display(text);
            }
            catch (Exception ex) { Log.Debug("SpeakCueOverlay.Show: {E}", ex.Message); }
        }

        /// <summary>Fade the cue out; the window closes when the fade ends. Safe from any thread.</summary>
        public static void HideCue()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(HideCue); return; }
            try { if (_active is { } w && w._state.Hide()) w.FadeTo(0); }
            catch (Exception ex) { Log.Debug("SpeakCueOverlay.Hide: {E}", ex.Message); }
        }

        /// <summary>Close at once, no fade (panic, shell teardown). Idempotent.</summary>
        public static void CloseActive()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(CloseActive); return; }
            try { _active?.CloseNow(); } catch (Exception ex) { Log.Debug("SpeakCueOverlay.Close: {E}", ex.Message); }
        }

        private readonly SpeakCueState _state = new();
        private readonly Grid _host = new() { IsHitTestVisible = false };
        private double _alpha, _from, _to;
        private long _start;
        private int _fade;   // bumps per fade, so a superseded fade's queued steps do nothing

        /// <summary>--render-all only: the sample cue at full alpha.</summary>
        internal SpeakCueOverlay() : this(live: false)
        {
            Width = 1280;
            Height = 720;
            _state.Show("Say YES");
            Build("Say YES", 0, SpeakCueState.TopOffsetDip, double.NaN);
        }

        private SpeakCueOverlay(bool live)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = _host;
            Closed += (_, _) => { _fade++; if (ReferenceEquals(_active, this)) _active = null; };
        }

        /// <summary>WPF SystemParameters.VirtualScreen*: every screen's bounds, in pixels.</summary>
        private PixelRect VirtualScreen()
        {
            var all = Screens?.All;
            if (all is null || all.Count == 0) return new PixelRect(0, 0, 1280, 720);
            return all.Skip(1).Aggregate(all[0].Bounds, (r, s) => r.Union(s.Bounds));
        }

        private void Display(string text)
        {
            if (!_state.Show(text)) return;
            var vs = VirtualScreen();
            var primary = Screens?.Primary;
            var scale = primary is { Scaling: > 0 } p ? p.Scaling : 1.0;
            var wa = primary?.WorkingArea ?? vs;
            var (left, top) = SpeakCueState.Anchor(wa.X / scale, wa.Y / scale, vs.X / scale, vs.Y / scale);
            Build(text, left, top, wa.Width / scale);
            FadeTo(SpeakCueState.PeakOpacity);
        }

        /// <summary>WPF DisplayCore: eyebrow + cue, centred over the primary work area's width.</summary>
        private void Build(string text, double left, double top, double width)
        {
            var eyebrow = new OutlinedTextBlock
            {
                Text = SpeakCueState.EyebrowText, FontSize = SpeakCueState.EyebrowFontSize, Fill = AccentFill,
                Stroke = StrokeBrush, StrokeThickness = SpeakCueState.EyebrowStroke,
                HorizontalAlignment = HorizontalAlignment.Center, Opacity = SpeakCueState.EyebrowOpacity,
            };
            eyebrow.Build();
            var cue = new OutlinedTextBlock
            {
                Text = text, FontSize = SpeakCueState.CueFontSize, Fill = CueFill,
                Stroke = StrokeBrush, StrokeThickness = SpeakCueState.CueStroke,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            };
            cue.Build();
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Children = { eyebrow, cue },
            };
            var anchor = new Grid
            {
                Width = width,
                HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(left, top, 0, 0),
                Children = { panel },
            };
            _host.Children.Clear();
            _host.Children.Add(anchor);
        }

        private void FadeTo(double target)
        {
            _from = _alpha;
            _to = target;
            _start = Clock.GetTimestamp();
            var fade = ++_fade;
            // A bounded ramp of one-shot steps (~60 Hz), as SubliminalOverlayWindow.Ramp: no frame loop.
            const int steps = 11;
            for (var i = 1; i <= steps; i++)
            {
                var last = i == steps;   // the last step always lands the fade, whatever the clock says
                DispatcherTimer.RunOnce(() => { if (fade == _fade && IsVisible) Step(last); },
                    TimeSpan.FromMilliseconds(SpeakCueState.FadeMs * i / (double)steps));
            }
            Step();
        }

        /// <summary>One fade step (queued by FadeTo; tests call it after stepping <see cref="Clock"/>).</summary>
        internal void Step(bool last = false)
        {
            var elapsed = Clock.GetElapsedTime(_start);
            if (last && elapsed.TotalMilliseconds < SpeakCueState.FadeMs) elapsed = TimeSpan.FromMilliseconds(SpeakCueState.FadeMs);
            _alpha = SpeakCueState.FadeAt(_from, _to, elapsed);
            ApplyAlpha(this, _alpha);
            if (elapsed.TotalMilliseconds < SpeakCueState.FadeMs) return;
            _fade++;
            if (_to == 0 && _state.FadedOut()) CloseNow();
        }

        internal double Alpha => _alpha;
        internal string? CueText => _state.Text;

        private void CloseNow()
        {
            _fade++;
            _host.Children.Clear();
            if (ReferenceEquals(_active, this)) _active = null;
            Close();
        }
    }
}
