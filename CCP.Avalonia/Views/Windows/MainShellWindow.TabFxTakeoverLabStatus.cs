// WPF MainWindow.TabFxTakeoverLabStatus.cs: the four status intents and global ambient gates.
// Avalonia uses sibling Border.BoxShadow layers on the shared beat; the feature owns the dot's
// fill, and the dot's opacity is pinned to 1 (only the sibling layer breathes). Lab remains retired, and the Takeover orb owns its own Skia clock.
// Awareness: AwarenessTabView.UpdateStatusIndicator calls SetAwarenessStatusPulse with the master
// switch, as WPF UpdateAwarenessStatusIndicator(masterOn) does (MainWindow.Awareness.cs:78,375).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Glow-breath bracket and period for every status dot. 2.8s is the slow end of
        /// "alive" and deliberately clear of the 0.8s blink the Blink Trainer used to run, which
        /// read as a warning light rather than as a heartbeat.</summary>
        private const double StatusPulseMinOpacity = 0.22;
        private const double StatusPulseMaxOpacity = 0.95;
        private const double StatusPulseSeconds = 2.8;

        /// <summary>Glow radius per dot size. The 8px Awareness dot and the 64px She's Listening
        /// disc cannot share one number without the small one turning into a smudge.</summary>
        private const double StatusPulseBlurSmall = 10;
        private const double StatusPulseBlurLarge = 26;

        private bool _pr4aFxInitialized;

        /// <summary>Every status dot currently asking for a pulse, and how it wants to look. The
        /// dictionary is the single record of intent; <see cref="ApplyStatusPulse"/> is the single
        /// place that turns intent into (or out of) a clock.</summary>
        private readonly Dictionary<Control, StatusPulseRequest> _statusPulses = new();

        private sealed class StatusPulseRequest
        {
            internal bool Wanted;
            internal double Blur;
            internal Color Tint;
            internal Border? Glow;
            internal IDisposable? Visibility;
            internal BeatLoop? Clock;
        }

        /// <summary>WPF focus, minimisation, motion and performance gates through the existing head environment.</summary>
        internal bool Pr4aAmbientAllowed => IsActive && WindowState != WindowState.Minimized
            && AmbientFxCanvas.Env.AllowAmbientLoops;

        /// <summary>The mod's glow colour, from the one theme key a mod switch rewrites. The
        /// fallback is the shipped FxGlowColor, so a missing key tints rather than throwing.</summary>
        private Color Pr4aGlowColor =>
            this.TryFindResource("FxGlowColor", out var found) && found is Color c
                ? c : Color.FromRgb(0xFF, 0x69, 0xB4);

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires this file's hooks once, from whichever status surface is refreshed first. Nothing
        /// a user who never opens one of those tabs has to pay for.
        /// </summary>
        private void EnsurePr4aFx()
        {
            if (_pr4aFxInitialized) return;
            _pr4aFxInitialized = true;
            try
            {
                // A plain Animation does not park itself on deactivate/minimise the way
                // AmbientFxCanvas does, so the pulses need this window funnel.
                Activated += OnPr4aFxWindowStateish;
                Deactivated += OnPr4aFxWindowStateish;
                PropertyChanged += OnPr4aFxWindowProperty;

                HookPr4aTab(Named<Control>("BlinkTrainerTab"));
                HookPr4aTab(Named<Control>("SheListeningTab"));
                HookPr4aTab(Named<Control>("AwarenessTab"));
                HookPr4aTab(StudioRack);
                HookPr4aTab(StudioRack?.HapticsPanel);
                AmbientFxCanvas.Env.MotionGateChanged += ApplyPr4aFxLoops;

                CoreMods.ModChanged += OnPr4aModChanged;
                // ModChanged is a STATIC event and this is an instance handler, so the
                // subscription would root the window for the life of the process.
                Closed += (_, _) =>
                {
                    CoreMods.ModChanged -= OnPr4aModChanged;
                    AmbientFxCanvas.Env.MotionGateChanged -= ApplyPr4aFxLoops;
                    foreach (var req in _statusPulses.Values)
                    {
                        StopStatusPulseClock(req);
                        req.Visibility?.Dispose();
                        if (req.Glow != null) req.Glow.IsVisible = false;
                    }
                    _statusPulses.Clear();
                };
            }
            catch (Exception ex) { Log.Warning(ex, "EnsurePr4aFx failed"); }
        }

        private void HookPr4aTab(Control? tab)
        {
            if (tab == null) return;
            tab.PropertyChanged -= OnPr4aTabPropertyChanged;
            tab.PropertyChanged += OnPr4aTabPropertyChanged;
        }

        private void OnPr4aFxWindowStateish(object? sender, EventArgs e) => ApplyPr4aFxLoops();

        private void OnPr4aFxWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty) ApplyPr4aFxLoops();
        }

        /// <summary>ShowTab writes IsVisible on the tab panel; that is the arrival and departure
        /// this reacts to.</summary>
        private void OnPr4aTabPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Visual.IsVisibleProperty) return;
            try
            {
                // WPF's arrival seed for an auto-connected device is not needed here: HapticsTabView's
                // attach (RefreshHapticToys -> RefreshHapticConnectionUi) reports it to the shell.
                ApplyPr4aFxLoops();
            }
            catch (Exception ex) { Log.Debug("OnPr4aTabPropertyChanged: {E}", ex.Message); }
        }

        /// <summary>A mod switch re-tints every status glow.</summary>
        private void OnPr4aModChanged(object? sender, ModPackage mod)
        {
            void Apply()
            {
                try
                {
                    var tint = Pr4aGlowColor;
                    foreach (var req in _statusPulses.Values)
                    {
                        req.Tint = tint;
                        if (req.Glow != null) PaintStatusGlow(req);
                    }
                    PremiumGateFx.RefreshAll();
                }
                catch (Exception ex) { Log.Debug("OnPr4aModChanged: {E}", ex.Message); }
            }

            try
            {
                if (Dispatcher.UIThread.CheckAccess()) Apply();
                else Dispatcher.UIThread.Post(Apply);
            }
            catch { /* a retint is never worth a throw */ }
        }

        /// <summary>Re-evaluates every loop this file owns against the current gate.</summary>
        private void ApplyPr4aFxLoops()
        {
            if (!_pr4aFxInitialized) return;
            try { ApplyPr4aStatusPulses(); global::ConditioningControlPanel.Avalonia.Controls.PremiumGateFx.RefreshAll(); }
            catch (Exception ex) { Log.Debug("ApplyPr4aFxLoops: {E}", ex.Message); }
        }

        // ============================== status pulses ==============================

        /// <summary>
        /// Records whether a status element wants to pulse and re-evaluates it. The ONLY entry
        /// point, so "the feature stopped" and "the window lost focus" land in the same place.
        /// </summary>
        private void SetStatusPulse(Control? dot, Border? glow, bool on, bool large = false)
        {
            if (dot == null || glow == null) return;
            try
            {
                if (!_statusPulses.TryGetValue(dot, out var req))
                {
                    if (!on) return;                 // nothing to remember about a dot that is off
                    _statusPulses[dot] = req = new StatusPulseRequest();
                }
                req.Wanted = on;
                req.Glow = glow;
                req.Blur = large ? StatusPulseBlurLarge : StatusPulseBlurSmall;
                req.Tint = Pr4aGlowColor;
                ApplyStatusPulse(dot, req);
            }
            catch (Exception ex) { Log.Debug("SetStatusPulse: {E}", ex.Message); }
        }

        private void ApplyPr4aStatusPulses()
        {
            try
            {
                foreach (var kvp in _statusPulses) ApplyStatusPulse(kvp.Key, kvp.Value);
            }
            catch (Exception ex) { Log.Debug("ApplyPr4aStatusPulses: {E}", ex.Message); }
        }

        /// <summary>Paints the sibling glow layer in the dot's tint, blur capped by the tier.</summary>
        private static void PaintStatusGlow(StatusPulseRequest req)
        {
            if (req.Glow == null) return;
            var tier = AmbientFxCanvas.Env.CurrentTier;
            req.Glow.Background = new SolidColorBrush(req.Tint);
            req.Glow.BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = req.Tint,
                Blur = Math.Min(req.Blur, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)),
            });
        }

        /// <summary>
        /// One dot, one decision. The pulse is a breathing sibling shadow BEHIND the dot rather than an
        /// opacity blink ON it: a status light that keeps disappearing reads as a fault, one that
        /// breathes reads as a pulse. Only the sibling layer's Opacity moves - an animated BlurRadius
        /// re-rasterises the kernel every frame, which is the expensive part.
        ///
        /// Off means the dot goes back to exactly what its own code painted: the local Effect is
        /// cleared and Opacity is 1. Nothing here ever touches Fill, so the tabs keep full
        /// ownership of colour.
        /// </summary>
        private void ApplyStatusPulse(Control dot, StatusPulseRequest req)
        {
            try
            {
                if (req.Glow is not { } glow) return;
                if (req.Visibility == null && dot.IsAttachedToVisualTree())
                    req.Visibility = EffectiveVisibility.Watch(dot, ApplyPr4aFxLoops);
                bool run = req.Wanted && dot.IsEffectivelyVisible && Pr4aAmbientAllowed
                    && AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier);
                // Neither the dot's fill nor its opacity carries the animation.
                dot.ClearValue(Visual.EffectProperty);
                dot.Opacity = 1;
                if (!run)
                {
                    StopStatusPulseClock(req);
                    glow.IsVisible = false;
                    glow.Opacity = 1;
                    return;
                }
                PaintStatusGlow(req);
                glow.IsVisible = true;
                if (req.Clock?.IsRunning == true) return;
                req.Clock = new BeatLoop(glow, t => glow.Opacity = Math.Clamp(
                    StatusPulseMinOpacity + (StatusPulseMaxOpacity - StatusPulseMinOpacity)
                    * BeatLoop.Breath(t, StatusPulseSeconds), 0, 1));
                req.Clock.Start();
            }
            catch (Exception ex) { Log.Debug("ApplyStatusPulse: {E}", ex.Message); }
        }

        private static void StopStatusPulseClock(StatusPulseRequest req)
        {
            var clock = req.Clock;
            if (clock == null) return;
            req.Clock = null;
            clock.Stop();
        }

        // ---- the four tabs' entry points -------------------------------------------

        /// <summary>Blink Trainer: the dot breathes while a session is actually RUNNING - never in
        /// IdleReady, which is precisely the state where nothing is happening.</summary>
        internal void SetBlinkTrainerStatusPulse(bool running)
        {
            EnsurePr4aFx();
            var tab = Named<Control>("BlinkTrainerTab");
            SetStatusPulse(tab?.FindControl<Control>("BlinkTrainerStatusDot"),
                tab?.FindControl<Border>("BlinkTrainerStatusGlow"), running);
        }

        /// <summary>Haptics: the connection dot breathes while a device is actually connected.</summary>
        internal void SetHapticsStatusPulse(bool connected)
        {
            EnsurePr4aFx();
            var tab = StudioRack?.HapticsPanel;
            SetStatusPulse(tab?.FindControl<Control>("HapticStatusDot"),
                tab?.FindControl<Border>("HapticStatusGlow"), connected);
        }

        /// <summary>She's Listening: the 64px mic disc breathes while the mic is armed.</summary>
        internal void SetSheListeningStatusPulse(bool armed)
        {
            EnsurePr4aFx();
            var tab = Named<Control>("SheListeningTab");
            SetStatusPulse(tab?.FindControl<Control>("SL_StatusDot"),
                tab?.FindControl<Border>("SL_StatusGlow"), armed, large: true);
        }

        /// <summary>Awareness: the dot breathes while the engine is live.</summary>
        internal void SetAwarenessStatusPulse(bool live)
        {
            EnsurePr4aFx();
            var tab = Named<Control>("AwarenessTab");
            SetStatusPulse(tab?.FindControl<Control>("AwarenessStatusDot"),
                tab?.FindControl<Border>("AwarenessStatusGlow"), live);
        }
    }
}
