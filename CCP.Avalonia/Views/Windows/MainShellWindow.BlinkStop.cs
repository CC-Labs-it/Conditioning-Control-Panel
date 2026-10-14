// PORTED from ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs (WPF 7.1.5) :156-283:
// WireRapidBlinkRecalibrateShortcut / TriggerRapidBlinkRecalibrateAsync / StopAllForRecalibration,
// gated by CCP.Core Services/Safety/BlinkStopGate (never more permissive than the panic key).
//
// The stop pass is exactly WPF StopAllForRecalibration's set (decision 2026-10-12 in
// docs/avalonia-decisions.md): a recalibration stop, not a panic. It pauses the session (never ends
// it) and leaves the camera, mic, haptics and program run alone. No leash service, Focus Gaze, help
// popover or interaction queue exists on this head, so those WPF steps have no target here.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // The detector's 500 ms cooldown makes 6 blinks span >= 2.5 s; 3.5 s is the achievable
        // window and far above the natural rate (~1 per 3-4 s). WPF LabTab.cs:163-164.
        internal const int RapidBlinkRecalCount = 6;
        internal const int RapidBlinkRecalWindowMs = 3500;
        private readonly Queue<DateTime> _rapidBlinkTimes = new();
        private bool _rapidBlinkRecalInProgress;

        /// <summary>The panic surfaces WPF StopAllForRecalibration (LabTab.cs:268-284) reaches: BlinkTrainer.Stop;
        /// KillAllAudio (App.xaml.cs:1745-1793: Mantra, Autonomy, LockCard.Stop(dismissOpenCards) at :1778,
        /// the tube's voice); Autonomy.CancelActivePulses; the session PAUSE + StopEngine (StartStop.cs:443-516:
        /// SkillTree, LockCardWindow.ForceCloseAll). Run in registry order.</summary>
        internal static readonly HashSet<string> BlinkStopIds = new()
            { "blink-trainer", "mantra", "takeover", "engine", "pink-rush", "lock-cards", "tube-speech" };

        /// <summary>Test seam: replaces the restart-camera + "Recalibrate?" offer after the stop pass.</summary>
        internal Func<Task>? BlinkRecalOfferForTests;

        private void InitializeRapidBlinkStop()
        {
            var tracker = WebcamTracker.Instance;
            Action onBlink = () => Dispatcher.UIThread.Post(() => OnRapidBlink(DateTime.UtcNow));
            Opened += (_, _) => { tracker.OnBlink -= onBlink; tracker.OnBlink += onBlink; };   // Opened fires on every Hide/Show
            Closed += (_, _) => tracker.OnBlink -= onBlink;
        }

        /// <summary>One blink (UI thread). True when this blink completed a run and fired the stop.</summary>
        internal bool OnRapidBlink(DateTime nowUtc)
        {
            // WPF :174-178: opt-in toggle; never while calibrating (its verify step asks for blinks) or mid-trigger.
            if (!CoreSettings.Current.BlinkRecalibrateShortcutEnabled) return false;
            if (_rapidBlinkRecalInProgress || WebcamCalibrationWindow.IsShowing) return false;

            var s = CoreSettings.Current;
            var block = BlinkStopGate.Check(   // WPF :186-190
                blinkTrainerRunning: Overlays.BlinkTrainerSession.IsRunning,
                lockdownActive: LockdownActive,
                panicKeyEnabled: s.PanicKeyEnabled,
                strictLockEnabled: s.StrictLockEnabled);
            if (block != BlinkStopGate.Block.None)
            {
                if (_rapidBlinkTimes.Count > 0) Log.Debug("Rapid-blink stop ignored: {Reason}", block);
                _rapidBlinkTimes.Clear();
                return false;
            }

            _rapidBlinkTimes.Enqueue(nowUtc);
            var cutoff = nowUtc.AddMilliseconds(-RapidBlinkRecalWindowMs);
            while (_rapidBlinkTimes.Count > 0 && _rapidBlinkTimes.Peek() < cutoff) _rapidBlinkTimes.Dequeue();
            if (_rapidBlinkTimes.Count < RapidBlinkRecalCount) return false;

            _rapidBlinkTimes.Clear();
            _ = TriggerRapidBlinkRecalibrateAsync();
            return true;
        }

        private async Task TriggerRapidBlinkRecalibrateAsync()
        {
            if (_rapidBlinkRecalInProgress) return;
            _rapidBlinkRecalInProgress = true;
            try
            {
                Log.Information("Rapid 6-blink gesture: stopping all activity and offering recalibration.");
                StopAllForRecalibration(this);
                if (BlinkRecalOfferForTests != null) { await BlinkRecalOfferForTests(); return; }

                // WPF :233-241: the capture loop may have stopped between the blink and here.
                var tracker = WebcamTracker.Instance;
                if (!tracker.IsRunning) await tracker.StartAsync();
                if (!tracker.IsRunning)
                {
                    Log.Warning("Rapid-blink recal: webcam not running and could not be (re)started; aborting.");
                    return;
                }

                // WPF :243-249 MessageBox YesNo, then the calibration window.
                if (!await Dialogs.MessageDialog.ConfirmAsync(this, "Recalibrate?",
                        "You blinked to stop everything. Recalibrate webcam tracking now?",
                        okText: "Yes", cancelText: "No"))
                    return;
                await WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
            }
            catch (Exception ex) { Log.Warning(ex, "Rapid-blink recalibration failed"); }
            finally
            {
                _rapidBlinkRecalInProgress = false;
                _rapidBlinkTimes.Clear();
            }
        }

        /// <summary>WPF StopAllForRecalibration (:268): the <see cref="BlinkStopIds"/> surfaces only.
        /// One failing stop never skips the rest.</summary>
        internal static void StopAllForRecalibration(MainShellWindow? shell)
        {
            foreach (var surface in PanicSurfaces.All.Where(x => BlinkStopIds.Contains(x.Id)))
            {
                try { surface.Stop(shell); }
                catch (Exception ex) { Log.Warning(ex, "Blink stop: {Id} failed", surface.Id); }
            }
        }
    }
}
