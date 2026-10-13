// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs StartMarqueeAnimation (:1139-1230) and
// ResolveDefaultMarqueeMessage; IMPORTED-AND-ADAPTED from trunk d30fcf576. Same as WPF: the saved message (blank -> the
// mod's banner -> the neutral default), upper-cased, doubled with 10 spaces into one segment, repeated to fill the
// canvas, scrolled 0 -> -segment at 80 px/s and looped; under the ambient gate one segment parks at X = 0. Differs: X
// is written from elapsed time on a BeatLoop (the window's shared 30 fps beat, parked while hidden), not a Storyboard.
// The Barnum interludes (WPF MainWindow.MarqueeReads.cs) ride on this strip: SettingsTabView.MarqueeReads.cs.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private MarqueeScroller? _marquee;

        /// <summary>The scrolling strip behind MarqueeText (tests read its state).</summary>
        internal MarqueeScroller Marquee => _marquee ??= new MarqueeScroller(MarqueeCanvas, MarqueeText);

        /// <summary>WPF MainWindow.StartMarqueeAnimation: rebuild the strip from the saved message.</summary>
        public void StartMarqueeAnimation() => Marquee.Start();

        private void InitializeMarqueeStrip()
        {
            var strip = Marquee;
            var reads = strip.Reads = new MarqueeInterlude(strip, MarqueeCanvas, MarqueeRead);
            EventHandler onLanguage = (_, _) => strip.Start();
            Action onGate = strip.Start;
            MarqueeText.AttachedToVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged += onLanguage;
                AmbientFxCanvas.Env.MotionGateChanged += onGate;
                strip.Start();
            };
            MarqueeText.DetachedFromVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged -= onLanguage;
                AmbientFxCanvas.Env.MotionGateChanged -= onGate;
                reads.Cancel();
                strip.Stop();
                reads.Disarm();
            };
            MarqueeCanvas.SizeChanged += (_, e) => { if (e.WidthChanged) strip.Start(); };
        }
    }

    /// <summary>WPF StartMarqueeAnimation over a Canvas + TextBlock pair, on a <see cref="BeatLoop"/>.</summary>
    internal sealed class MarqueeScroller
    {
        /// <summary>WPF: "Speed: 80 pixels per second".</summary>
        internal const double PixelsPerSecond = 80;

        /// <summary>WPF: 10 spaces between repetitions.</summary>
        internal const string Separator = "          ";

        private readonly Canvas _canvas;
        private readonly TextBlock _text;
        private readonly BeatLoop _loop;
        private double _phase, _t;   // seconds of scroll banked by a Pause, and the beat's last t
        private bool _paused;

        /// <summary>The interludes this strip arms (WPF ArmMarqueeReadClock / CancelMarqueeInterlude).</summary>
        internal MarqueeInterlude? Reads { get; set; }

        public MarqueeScroller(Canvas canvas, TextBlock text)
        {
            _canvas = canvas;
            _text = text;
            _loop = new BeatLoop(text, t => { _t = t; if (SegmentWidth > 0) Transform.X = -((_phase + t) * PixelsPerSecond % SegmentWidth); });
        }

        /// <summary>Started and not parked by the ambient gate.</summary>
        internal bool IsScrolling => _loop.IsRunning;

        /// <summary>The beat's clock is subscribed: false while Home is hidden (P01).</summary>
        internal bool IsTicking => _loop.IsTicking;

        /// <summary>The upper-cased message the strip was last built from.</summary>
        internal string CurrentMessage { get; private set; } = "";

        internal double SegmentWidth { get; private set; }

        internal TranslateTransform Transform => _text.RenderTransform as TranslateTransform
            ?? (TranslateTransform)(_text.RenderTransform = new TranslateTransform());

        /// <summary>WPF ResolveDefaultMarqueeMessage: the active mod's banner, else the neutral const.</summary>
        internal static string ResolveDefaultMarqueeMessage()
        {
            try
            {
                var banner = App.Mods?.GetMarqueeBannerMessage();
                if (!string.IsNullOrWhiteSpace(banner)) return banner!;
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Marquee: mod banner lookup failed; using the neutral default"); }
            return AppSettings.DefaultMarqueeMessage;
        }

        public void Stop() => _loop.Stop();

        /// <summary>WPF Storyboard.Pause: an interlude holds the strip where it is.</summary>
        internal void Pause()
        {
            if (!_loop.IsRunning) return;
            _paused = true;
            _phase += _t;
            _loop.Stop();
        }

        /// <summary>WPF Storyboard.Resume: carry on from the held frame, not from zero.</summary>
        internal void Resume()
        {
            if (!_paused) return;
            _paused = false;
            _loop.Start();
        }

        public void Start()
        {
            try
            {
                Reads?.Cancel();   // WPF: the rebuild takes the stage back from any act
                Stop();
                _phase = _t = 0;
                _paused = false;
                var canvasWidth = _canvas.Bounds.Width;
                if (canvasWidth <= 0) return;

                var message = CoreSettings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(message)) message = ResolveDefaultMarqueeMessage();
                CurrentMessage = message = message.ToUpperInvariant();
                var segment = message + Separator + message + Separator;

                var probe = new TextBlock { Text = segment, FontFamily = _text.FontFamily, FontSize = _text.FontSize, FontWeight = _text.FontWeight };
                probe.Measure(Size.Infinity);
                if (probe.DesiredSize.Width <= 0) return;
                SegmentWidth = probe.DesiredSize.Width;

                Transform.X = 0;
                if (!AmbientFxCanvas.Env.AllowAmbientLoops)
                {
                    _text.Text = segment;   // parked: one segment at the origin
                    Reads?.Arm(0);
                    return;
                }
                _text.Text = string.Concat(Enumerable.Repeat(segment, (int)Math.Ceiling(canvasWidth / SegmentWidth) + 2));
                _loop.Start();
                Reads?.Arm(SegmentWidth / PixelsPerSecond);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to start marquee animation"); }
        }
    }
}
