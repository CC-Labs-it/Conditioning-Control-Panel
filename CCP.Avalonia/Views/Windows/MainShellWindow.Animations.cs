// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Animations.cs (199 lines).
//
// The Lockdown plate's pink breath (WPF :85-113): #FF1493 <-> #FF69B4 on the border, blur 12 <-> 22
// and glow opacity 0.7 <-> 1.0, linear, AutoReverse, 1.5 s, Forever at AmbientFrameRate 30. It rides
// the window's 30 fps beat (BeatLoop) instead of an infinite Animation (lag fix, P82), which also
// parks it while the plate is hidden or the window minimised. Durations, amplitudes and colours verbatim.
//
// Avalonia cannot put an x:Name on a brush or an effect (AVLN2000), so LockdownImageBorderBrush and
// LockdownImageGlow are reached as LockdownImageBorder's own BorderBrush and Effect. A XAML attribute
// brush is immutable, so the start swaps in a mutable SolidColorBrush and the stop puts the original back.
//
// ShowTab stops it on every navigation and its "lockdown" case starts it, as WPF does
// (MainWindow.TabNavigation.cs:306/677; MainShellWindow.TabNavigation.cs).
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

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const double LockdownPulseSeconds = 1.5;   // WPF :100-106
        private BeatLoop? _lockdownPulseClock;

        /// <summary>The brush the Lockdown plate shipped with, kept so the stop puts back the
        /// exact object rather than a colour that happens to match.</summary>
        private IBrush? _lockdownRestingBrush;

        internal bool LockdownPulsing => _lockdownPulseClock?.IsRunning == true;

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
                _lockdownRestingBrush ??= plate.BorderBrush;
                var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x14, 0x93));
                plate.BorderBrush = brush;
                var glow = plate.Effect as DropShadowEffect;

                _lockdownPulseClock = new BeatLoop(plate, t =>
                {
                    // Linear AutoReverse: 0 -> 1 over 1.5 s, back over the next 1.5 s.
                    double p = t / LockdownPulseSeconds % 2, k = p <= 1 ? p : 2 - p;
                    brush.Color = Color.FromRgb(0xFF, (byte)Math.Round(0x14 + (0x69 - 0x14) * k), (byte)Math.Round(0x93 + (0xB4 - 0x93) * k));
                    if (glow != null) { glow.BlurRadius = 12 + 10 * k; glow.Opacity = 0.7 + 0.3 * k; }
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
                if (plate.Effect is DropShadowEffect glow) { glow.BlurRadius = 14; glow.Opacity = 0.7; }
            }
            catch (Exception ex) { Log.Debug("StopLockdownPulse: {E}", ex.Message); }
        }
    }
}
