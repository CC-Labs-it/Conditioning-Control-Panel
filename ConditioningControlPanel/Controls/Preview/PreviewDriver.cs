using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview
{
    /// <summary>How the preview may move right now.</summary>
    public enum PreviewPace
    {
        /// <summary>The engine or a session runs (the panel stands down): nothing moves, nothing settles.</summary>
        Hold,
        /// <summary>Motion Off, or a performance tier without ambient motion: a change jumps straight to the settled still.</summary>
        Still,
        /// <summary>It plays for <see cref="PreviewDriver.WakeSeconds"/> after a touch, then holds still.</summary>
        Live,
    }

    /// <summary>The pure gate of the preview (copied in spirit from AmbientFxCanvas.ShouldRun).</summary>
    public static class PreviewGate
    {
        /// <summary>
        /// Full plays only when <c>MotionFx.AllowAmbientLoops</c> says so. Reduced plays too, because the
        /// preview only moves after the user touched something (it is not an ambient loop), with drift off
        /// and fades at the gentle pace; a tier without ambient motion still gets the still. Off never plays.
        /// </summary>
        public static PreviewPace Pace(MotionLevel level, bool panelQuiet, bool allowAmbientLoops, bool tierAllowsMotion)
        {
            if (panelQuiet) return PreviewPace.Hold;
            return level switch
            {
                MotionLevel.Full => allowAmbientLoops ? PreviewPace.Live : PreviewPace.Still,
                MotionLevel.Reduced => tierAllowsMotion ? PreviewPace.Live : PreviewPace.Still,
                _ => PreviewPace.Still,
            };
        }

        /// <summary>The clock may tick only while the preview is loaded, visible and its window is in front.</summary>
        public static bool CanShow(bool loaded, bool visible, bool windowActive, bool minimized)
            => loaded && visible && windowActive && !minimized;
    }

    /// <summary>
    /// Wakes when touched: the scene plays for <see cref="WakeSeconds"/> after a watched setting changes, a
    /// click lands in it or the page opens, then holds still on whatever is on screen. No Forever loop: the
    /// host's frame clock runs only while <see cref="WantsClock"/>. The clock is injected (tests).
    /// </summary>
    public sealed class PreviewDriver
    {
        public const double WakeSeconds = 10.0;
        /// <summary>How far a still is run forward when it cannot animate (Motion Off).</summary>
        public const double SettleSeconds = 1.5;
        public const double StepSeconds = 1.0 / 30.0;
        private const double MaxDt = 0.1;

        private readonly IPreviewScene _scene;
        private readonly Func<double> _now;
        private readonly HashSet<string> _watched;
        private double _awakeUntil = double.NegativeInfinity;
        private double _last = double.NaN;

        public PreviewDriver(IPreviewScene scene, Func<double> nowSeconds)
        {
            _scene = scene;
            _now = nowSeconds;
            _watched = new HashSet<string>(scene.Settings, StringComparer.Ordinal);
        }

        public IPreviewScene Scene => _scene;
        public PreviewPace Pace { get; set; } = PreviewPace.Live;
        /// <summary>Loaded, visible, window active (see <see cref="PreviewGate.CanShow"/>).</summary>
        public bool CanShow { get; set; } = true;
        /// <summary>Scene ticks driven by the clock (settles not counted).</summary>
        public int Ticks { get; private set; }

        public bool Watches(string name) => _watched.Contains(name);
        public bool IsAwake => _now() < _awakeUntil;
        public bool WantsClock => Pace == PreviewPace.Live && CanShow && IsAwake;

        /// <summary>Start the wake window. Returns true when the host should repaint now.</summary>
        public bool Wake()
        {
            _awakeUntil = _now() + WakeSeconds;
            _last = double.NaN;
            switch (Pace)
            {
                case PreviewPace.Still: Settle(); return true;
                case PreviewPace.Live: return true;
                default: return false;
            }
        }

        /// <summary>The page opened.</summary>
        public bool Open()
        {
            _scene.Start();
            return Wake();
        }

        /// <summary>An AppSettings property changed. Returns true when the host should repaint.</summary>
        public bool SettingChanged(string name)
        {
            if (string.IsNullOrEmpty(name) || !_watched.Contains(name)) return false;
            _scene.SettingChanged(name);
            return Wake();
        }

        /// <summary>A click in the screen (screen DIPs). Returns true when the host should repaint.</summary>
        public bool Click(SKPoint at)
        {
            if (Pace == PreviewPace.Hold) return false;
            _scene.Click(at);
            return Wake();
        }

        /// <summary>One frame of the host's clock. Returns true when the scene moved (repaint).</summary>
        public bool Frame()
        {
            if (!WantsClock) { _last = double.NaN; return false; }
            var now = _now();
            var dt = double.IsNaN(_last) ? StepSeconds : Math.Clamp(now - _last, 0, MaxDt);
            _last = now;
            _scene.Tick(dt);
            Ticks++;
            return true;
        }

        private void Settle()
        {
            for (double s = 0; s < SettleSeconds; s += StepSeconds) _scene.Tick(StepSeconds);
        }
    }
}
