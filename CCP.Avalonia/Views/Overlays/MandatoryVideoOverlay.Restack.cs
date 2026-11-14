using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    internal sealed partial class MandatoryVideoOverlay
    {
        internal static void RestackOnVideoStart()
        {
            Instance.RestackGameAndTargets();
            ScheduleGameRestack(Instance.RestackGameAndTargets, TimeSpan.FromMilliseconds(60));
        }
        internal static void RestackOnVideoClick() =>
            ScheduleGameRestack(Instance.RestackGameAndTargets, TimeSpan.FromMilliseconds(50));

        private void RestackGameAndTargets()
        {
            // WPF ChaosModeService.cs:745: gameplay rides above the video, except while a fallback
            // target shares the opaque video window (then the video must sit on top until it retires).
            if (!_surfaces.Any(x => x.AttentionWindow == null && x.Layer.Children.Count > 0))
                ChaosRunHost.RaiseGameLayerAboveVideo();
            foreach (var x in _surfaces)
                if (x.Layer.Children.Count > 0)
                {
                    // A fallback target shares the opaque video window, so the whole video must
                    // sit above gameplay until that target retires (WPF VideoService.cs:5822).
                    var w = x.AttentionWindow ?? x.Window;
                    if (w.IsVisible)
                        try { RaiseAttentionWindow(w); } catch (Exception ex) { Log.Debug("Attention raise: {E}", ex.Message); }
                }
        }

    }
}
