// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Animations.cs (199 lines).
//
// The Lockdown plate's pink breath (WPF :85-113): #FF1493 <-> #FF69B4 on the border, blur 12 <-> 22
// and glow opacity 0.7 <-> 1.0, linear, AutoReverse, 1.5 s, at AmbientFrameRate 30. It rides
// the window's 30 fps beat (BeatLoop) instead of an infinite Animation (lag fix, P82), which also
// parks it while the plate is hidden or the window minimised. Durations, amplitudes and colours verbatim.
//
// ShowTab starts the pulse on Lockdown arrival and stops it on departure, as WPF does. The
// glow lives on a sibling Border: an animated Effect would re-render the plate's contents.
// A XAML attribute brush is immutable, so the start swaps in a mutable SolidColorBrush;
// stop restores the original brush, shadow and opacity. The shared beat parks while minimised.
// Settings > Performance stops the pulse at Reduced/Off; WPF re-arms it on the next
// tab visit, rather than immediately on a switch to Full.
//
// Dropped, with the reason:
//   StartSeasonTitleShimmer / StopSeasonTitleShimmer (WPF :35-83)
//     PORTED ELSEWHERE: Views/Tabs/QuestsTabView.Fx.cs runs the sweep and the glow breath on the tab's
//     own clock while the tab is shown. The uncalled window-side copy here was a second, infinite one.
//   StopSkillTreeAnimations / RestartSkillTreeAnimations
//     SUPERSEDED: the tab's ambient air is an AmbientFxCanvas that parks and resumes itself
//     (MainShellWindow.EnhancementsFx.cs); there is nothing here for a stop/restart pair to own.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BeatLoop? _lockdownPulseClock;

        /// <summary>The brush the Lockdown plate shipped with, kept so the stop puts back the
        /// exact object rather than a colour that happens to match.</summary>
        private IBrush? _lockdownRestingBrush;
        private BoxShadows _lockdownRestingShadow;
        private double _lockdownRestingOpacity;

        internal bool LockdownPulsing => _lockdownPulseClock?.IsRunning == true;

        private void ApplyLockdownPulse(string tab)
        {
            // WPF stops on every switch (:305) and starts again on Lockdown (:677), so re-selecting
            // Lockdown restarts the pulse from #FF1493.
            StopLockdownPulse();
            if (tab == "lockdown" && global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowAmbientLoops)
                StartLockdownPulse();
        }

        internal void StartLockdownPulse()
        {
            if (_lockdownPulseClock != null) return;                 // already running
            // WPF :88: no pulse when ambient loops are off (Motion Reduced/Off, performance tier).
            if (!global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowAmbientLoops) return;
            try
            {
                var plate = Named<Control>("LockdownTab")?.FindControl<Border>("LockdownImageBorder");
                if (plate == null) return;

                // See the header: the XAML brush is immutable, so the loop needs one of its own.
                _lockdownRestingBrush = plate.BorderBrush;
                var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x14, 0x93));
                plate.BorderBrush = brush;
                var glow = Named<Control>("LockdownTab")?.FindControl<Border>("LockdownImageGlow");
                if (glow != null)
                {
                    _lockdownRestingShadow = glow.BoxShadow;
                    _lockdownRestingOpacity = glow.Opacity;
                }

                _lockdownPulseClock = new BeatLoop(plate, t =>
                {
                    var frame = LockdownPulse.At(t);
                    brush.Color = Color.FromRgb(frame.R, frame.G, frame.B);
                    if (glow != null)
                    {
                        glow.BoxShadow = new BoxShadows(new BoxShadow
                        {
                            Color = Color.FromRgb(255, 20, 147), Blur = frame.Blur,
                        });
                        glow.Opacity = frame.Opacity;
                    }
                });
                _lockdownPulseClock.Start();
            }
            catch (Exception ex) { Log.Warning("Failed to start lockdown pulse: {Error}", ex.Message); }
        }

        internal void StopLockdownPulse()
        {
            var clock = _lockdownPulseClock;
            if (clock == null) return;
            _lockdownPulseClock = null;
            try
            {
                clock.Stop();
                var plate = Named<Control>("LockdownTab")?.FindControl<Border>("LockdownImageBorder");
                if (plate == null) return;
                if (_lockdownRestingBrush != null) plate.BorderBrush = _lockdownRestingBrush;
                if (Named<Control>("LockdownTab")?.FindControl<Border>("LockdownImageGlow") is { } glow)
                {
                    glow.BoxShadow = _lockdownRestingShadow;
                    glow.Opacity = _lockdownRestingOpacity;
                }
            }
            catch (Exception ex) { Log.Debug("StopLockdownPulse: {E}", ex.Message); }
        }
    }
}
