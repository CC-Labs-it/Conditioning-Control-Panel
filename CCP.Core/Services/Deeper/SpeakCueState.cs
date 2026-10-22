using System;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// The rules of the persistent voice-prompt cue (WPF <c>ConditioningControlPanel/Overlays/SpeakCueOverlay.cs</c>),
    /// kept free of any UI so every head shows, updates and fades it the same way. A head owns one of these
    /// per cue window and asks it what to do; the window only draws.
    /// </summary>
    public sealed class SpeakCueState
    {
        /// <summary>WPF FADE_MS: both the fade in and the fade out.</summary>
        public const int FadeMs = 180;
        /// <summary>WPF PEAK_OPACITY: the cue never goes fully opaque.</summary>
        public const double PeakOpacity = 0.92;
        /// <summary>WPF CUE_FONT.</summary>
        public const double CueFontSize = 60;
        /// <summary>WPF TOP_OFFSET_DIP: below the top of the primary work area.</summary>
        public const double TopOffsetDip = 120;
        /// <summary>The eyebrow line above the cue (WPF hardcodes it in English, so does this).</summary>
        public const string EyebrowText = "SAY IT";
        public const double EyebrowFontSize = 22;
        public const double EyebrowOpacity = 0.8;
        public const double EyebrowStroke = 2.0;
        public const double CueStroke = 3.0;

        /// <summary>The cue on screen (or fading out); null once the fade out has finished.</summary>
        public string? Text { get; private set; }

        /// <summary>True from <see cref="Show"/> to <see cref="Hide"/>: the cue is, or is fading, in.</summary>
        public bool Visible { get; private set; }

        /// <summary>WPF ShowCue: blank text is ignored (false); anything else shows or replaces the cue.</summary>
        public bool Show(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            Text = text;
            Visible = true;
            return true;
        }

        /// <summary>WPF HideCue: start the fade out. False when nothing is showing.</summary>
        public bool Hide()
        {
            if (!Visible) return false;
            Visible = false;
            return true;
        }

        /// <summary>The fade out finished. True means drop the window; false means a <see cref="Show"/>
        /// landed mid-fade (WPF's new fade-in replaces the fade-out, whose Completed never runs).</summary>
        public bool FadedOut()
        {
            if (Visible) return false;
            Text = null;
            return true;
        }

        /// <summary>Linear opacity <paramref name="elapsed"/> into a <see cref="FadeMs"/> fade (WPF DoubleAnimation).</summary>
        public static double FadeAt(double from, double to, TimeSpan elapsed)
        {
            var t = Math.Clamp(elapsed.TotalMilliseconds / FadeMs, 0, 1);
            return from + (to - from) * t;
        }

        /// <summary>Where the cue's column starts inside the virtual-screen window, in DIP: the primary
        /// work area's left/top relative to the virtual screen (never negative), plus <see cref="TopOffsetDip"/>.</summary>
        public static (double Left, double Top) Anchor(double workLeft, double workTop, double virtualLeft, double virtualTop)
            => (Math.Max(0, workLeft - virtualLeft), Math.Max(0, workTop - virtualTop) + TopOffsetDip);
    }
}
