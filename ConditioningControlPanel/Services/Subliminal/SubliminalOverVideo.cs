using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Where a subliminal card goes while a mandatory video is on screen (ccp-bugs #1422).
    ///
    /// The cause: with the unified overlay host on (the default), a card is a layer on the
    /// compositor host, and OverlayService.ReassertZOrder pins that host directly BELOW the video
    /// window (#497, so the spiral and the pink filter never cover the clip). The words rode the
    /// host down with them, so nothing a controller sent by Remote Control could be read during a
    /// video. The words are not a filter: they are meant to be read.
    ///
    /// The rule, in two parts:
    ///  1. While a video window is up, a card leaves the compositor host and uses the service's own
    ///     per-screen window (<see cref="UsesCompositorCard"/>).
    ///  2. That window is slotted DIRECTLY ABOVE the video window on its monitor, not at the top of
    ///     the topmost band (<see cref="ResolveSlot"/>). Everything that already sat above the video
    ///     (a lock card, the Lockdown veil and its exit phrase, a panic surface, the video's own
    ///     attention targets) keeps its place above the words.
    ///
    /// Pure and window-free so both halves are unit-testable.
    /// </summary>
    internal static class SubliminalOverVideo
    {
        /// <summary>True when the card may render as a compositor layer. Focus-steal never could
        /// (the host is NOACTIVATE); a live video window now also sends it to the classic window.</summary>
        internal static bool UsesCompositorCard(bool compositorOn, bool stealsFocus, bool videoWindowUp) =>
            compositorOn && !stealsFocus && !videoWindowUp;

        internal enum SlotKind
        {
            /// <summary>No video window in the band: top of the topmost band, as before.</summary>
            TopOfBand,
            /// <summary>Already directly above the video: leave the z-order alone.</summary>
            Keep,
            /// <summary>Insert directly below <see cref="Slot.InsertAfter"/>, which is the window
            /// sitting just above the video.</summary>
            Below
        }

        internal readonly record struct Slot(SlotKind Kind, IntPtr InsertAfter);

        /// <summary>
        /// Pick the z slot for one subliminal window.
        /// </summary>
        /// <param name="bandTopDown">The topmost band, front window first (hidden windows included).</param>
        /// <param name="videos">Every live video window with the monitor it covers.</param>
        /// <param name="self">The subliminal window being placed.</param>
        /// <param name="selfMonitor">The monitor that window covers.</param>
        internal static Slot ResolveSlot(IReadOnlyList<IntPtr> bandTopDown,
            IReadOnlyList<(IntPtr Hwnd, IntPtr Monitor)> videos, IntPtr self, IntPtr selfMonitor)
        {
            if (bandTopDown == null || videos == null || videos.Count == 0)
                return new Slot(SlotKind.TopOfBand, IntPtr.Zero);

            // The front-most video window on this monitor; with none here (a one-screen video on a
            // two-screen desk) the front-most video window anywhere, so the card still never jumps
            // over whatever sits above the clip.
            int anchor = IndexOfFrontVideo(bandTopDown, videos, selfMonitor, sameMonitorOnly: true);
            if (anchor < 0) anchor = IndexOfFrontVideo(bandTopDown, videos, selfMonitor, sameMonitorOnly: false);
            if (anchor < 0) return new Slot(SlotKind.TopOfBand, IntPtr.Zero);

            if (anchor > 0 && bandTopDown[anchor - 1] == self)
                return new Slot(SlotKind.Keep, IntPtr.Zero);

            // The nearest window above the video that is not the card itself.
            for (int i = anchor - 1; i >= 0; i--)
            {
                if (bandTopDown[i] == self || bandTopDown[i] == IntPtr.Zero) continue;
                return new Slot(SlotKind.Below, bandTopDown[i]);
            }
            // The video is the front window: the top of the band IS directly above it.
            return new Slot(SlotKind.TopOfBand, IntPtr.Zero);
        }

        private static int IndexOfFrontVideo(IReadOnlyList<IntPtr> bandTopDown,
            IReadOnlyList<(IntPtr Hwnd, IntPtr Monitor)> videos, IntPtr monitor, bool sameMonitorOnly)
        {
            for (int i = 0; i < bandTopDown.Count; i++)
            {
                var h = bandTopDown[i];
                if (h == IntPtr.Zero) continue;
                for (int v = 0; v < videos.Count; v++)
                {
                    if (videos[v].Hwnd != h) continue;
                    if (!sameMonitorOnly || (monitor != IntPtr.Zero && videos[v].Monitor == monitor)) return i;
                }
            }
            return -1;
        }
    }
}
