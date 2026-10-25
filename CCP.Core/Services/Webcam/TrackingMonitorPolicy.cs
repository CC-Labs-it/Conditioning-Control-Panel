using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Webcam
{
    /// <summary>
    /// The "Tracking monitor" pick (AppSettings.WebcamCalibrationScreen) over a head's screen list,
    /// by index so it needs no UI types. WPF twins: App.GetWebcamCalibrationScreen (App.xaml.cs:1408)
    /// and GazeContentScreenPolicy.ResolveGazeReactiveScreens (Services/Tracking/GazeContentScreenPolicy.cs:34).
    /// </summary>
    public static class TrackingMonitorPolicy
    {
        /// <summary>The default sentinel: no explicit pick, follow the primary screen.</summary>
        public const string Primary = "Primary";

        /// <summary>True when <paramref name="saved"/> names a display rather than the sentinel.</summary>
        public static bool IsExplicitPick(string? saved) =>
            !string.IsNullOrEmpty(saved) && !string.Equals(saved, Primary, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The screen the calibration, Quick Recal and Tracker Test windows open on: the named one,
        /// else the primary (sentinel, or the named display is gone). -1 only for an empty list.
        /// </summary>
        public static int CalibrationIndex(string? saved, IReadOnlyList<string> names, int primaryIndex)
        {
            if (names.Count == 0) return -1;
            if (primaryIndex < 0 || primaryIndex >= names.Count) primaryIndex = 0;
            if (!IsExplicitPick(saved)) return primaryIndex;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], saved, StringComparison.OrdinalIgnoreCase)) return i;
            return primaryIndex;
        }

        /// <summary>
        /// The screens a gaze-reactive overlay (the Blink Trainer session) covers. An explicit pick wins
        /// outright, even over DualMonitorEnabled; an explicit pick that is unplugged falls back to the
        /// primary alone, as WPF does. The sentinel follows the global rule: dual ? every screen : primary.
        /// </summary>
        public static int[] OverlayIndices(string? saved, IReadOnlyList<string> names, int primaryIndex, bool dualMonitorEnabled)
        {
            if (names.Count == 0) return Array.Empty<int>();
            if (IsExplicitPick(saved)) return new[] { CalibrationIndex(saved, names, primaryIndex) };
            if (dualMonitorEnabled) return Enumerable.Range(0, names.Count).ToArray();
            return new[] { CalibrationIndex(null, names, primaryIndex) };
        }
    }
}
