using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Video.Floating
{
    /// <summary>
    /// When a floating video window may be seen (owner, 2026-10-19: for a second or two while a
    /// clip loaded, its square was a black rectangle). The window exists from the start, because a
    /// WebView2 only initialises inside a shown window, but it stays veiled (a one-pixel
    /// window region: nothing to see or click) until its picture is painted. A clip that never gets there is
    /// skipped and its window closed, never left on screen. Pure, pinned by FloatingVideoPolishTests.
    /// </summary>
    internal static class FloatingFirstFrame
    {
        /// <summary>From the page's ready handshake: this long for a first frame, then the clip is skipped.</summary>
        public const int ReadyBudgetMs = 4000;

        /// <summary>No frame report (a page without requestVideoFrameCallback, or one Chromium treats
        /// as hidden): the clip counts as painted once it has been playing this long...</summary>
        public const int PlayingGraceMs = 1000;

        /// <summary>...and this much of it has played since "playing".</summary>
        public const int AdvanceMs = 400;

        internal enum Verdict { Wait, Reveal, Skip }

        /// <param name="frameSeen">The page reported its picture painted: several presented frames with
        /// the media time moving, on a visible page (player.js announceFirstFrame). One callback was
        /// not enough: the owner saw the window open empty and the picture come a second later.</param>
        /// <param name="msSincePlaying">Since the page reported "playing", or null before it.</param>
        /// <param name="advancedMs">How much has played since "playing" (seeks excluded).</param>
        /// <param name="msSinceReady">Since the page's ready handshake, or null before it (the
        /// WebView2 start-up has its own, longer watch).</param>
        public static Verdict Decide(bool frameSeen, long? msSincePlaying, long advancedMs, long? msSinceReady)
        {
            if (frameSeen) return Verdict.Reveal;
            if (msSincePlaying >= PlayingGraceMs && advancedMs >= AdvanceMs) return Verdict.Reveal;
            if (msSincePlaying != null) return Verdict.Wait;   // playing: the grace above decides, never a skip
            if (msSinceReady >= ReadyBudgetMs) return Verdict.Skip;
            return Verdict.Wait;
        }
    }

    /// <summary>
    /// How a floating video window comes and goes. These windows can never be faded: a WebView2
    /// does not paint inside a layered window (BrowserVideoWindow's hard rule) and a LibVLC
    /// VideoView is a native child HWND, so window opacity is not available. The window's region
    /// is animated instead: it opens from the centre (a quick ease-out grow from
    /// <see cref="RevealFrom"/> of its size) and closes into the centre (ease-in). Under reduced
    /// motion (MotionFx below Full, which Gentle visuals always is) there is no animation at all:
    /// the window appears and goes. Pure, pinned by FloatingVideoPolishTests.
    /// </summary>
    internal static class FloatingSpawnLook
    {
        public const int RevealMs = 260;
        public const int VanishMs = 180;
        public const double RevealFrom = 0.7;

        public static bool Animates(MotionLevel level) => level == MotionLevel.Full;

        public static double EaseOut(double t) { t = Math.Clamp(t, 0, 1); return 1 - Math.Pow(1 - t, 3); }

        public static double EaseIn(double t) { t = Math.Clamp(t, 0, 1); return t * t * t; }

        /// <summary>The visible share of the window, 0..1, this far into the reveal.</summary>
        public static double RevealScale(double elapsedMs)
            => RevealFrom + (1 - RevealFrom) * EaseOut(elapsedMs / RevealMs);

        /// <summary>The visible share of the window, 1..0, this far into the close.</summary>
        public static double VanishScale(double elapsedMs) => 1 - EaseIn(elapsedMs / VanishMs);

        /// <summary>The centred rectangle (window coordinates) showing <paramref name="scale"/> of a
        /// w x h window. Scale 1 is the whole window, 0 an empty rectangle at the centre.</summary>
        public static (int Left, int Top, int Right, int Bottom) Visible(int w, int h, double scale)
        {
            scale = Math.Clamp(scale, 0, 1);
            int vw = (int)Math.Round(w * scale), vh = (int)Math.Round(h * scale);
            int left = (w - vw) / 2, top = (h - vh) / 2;
            return (left, top, left + vw, top + vh);
        }
    }
}
