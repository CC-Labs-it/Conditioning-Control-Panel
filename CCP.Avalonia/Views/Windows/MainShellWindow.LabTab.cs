// WPF MainWindow.LabTab.cs glue. DevicesSettingsSection owns the camera/monitor pickers,
// consent/calibration/test/recalibration handlers and diagnostics; the status chips are shared
// through WebcamStatusPresentation. SafetyPills and BlinkStop own the privacy stop/6-blink gesture.
// ponytail: GazeRefineOverlay, the gaze cursor/lock/drift services, Quick Recal global hotkeys,
// mouth/tongue diagnostics and the capture probe's in-use distinction remain unported.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF HookFocusGazeService (LabTab.cs:819): the engine follows the camera for this
        /// window's life, and the saved switch intent is restored (MasterEnabled + the no-prompt boot
        /// camera restart). The Play tab's switch and status line read the same engine.</summary>
        private void InitializeFocusGaze()
        {
            var gaze = Platform.GazeFocus.Instance;
            gaze.Attach();
            // WPF: Closing += GazeFocus.Stop, so nothing it holds outlives the window.
            Closed += (_, _) => gaze.Detach();
            if (!CoreSettings.Current.FocusGazeEnabled) return;
            gaze.MasterEnabled = true;
            _ = RestartFocusGazeCameraAtBootAsync();
        }

        /// <summary>WPF RestartFocusGazeCameraAtBoot (#1106): opens the camera behind the switch's own
        /// gates and never asks for consent at boot; a missing tier, consent or calibration leaves the
        /// status on "waiting" and the switch does the asking. The engine re-evaluates on the camera's
        /// own state change.</summary>
        internal static async System.Threading.Tasks.Task RestartFocusGazeCameraAtBootAsync()
        {
            try
            {
                var webcam = Platform.WebcamTracker.Instance;
                if (webcam.IsRunning) return;
                if (!Services.TierGate.RequiresLab(global::ConditioningControlPanel.Localization.Loc.Get("label_focus_gaze")).Allowed) return;
                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                if (!CoreSettings.Current.WebcamCalibrated) return;
                if (!await webcam.StartAsync())
                    Serilog.Log.Information("Focus Gaze: webcam did not start at boot ({State})", webcam.State);
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Focus Gaze: webcam start at boot failed"); }
        }

        // WPF MainWindow.LabTab.cs MicActivePill_Click: the mic's privacy stop. Not Lockdown-gated, as WPF.
        private void MicActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is global::Avalonia.Visual v && !e.GetCurrentPoint(v).Properties.IsLeftButtonPressed) return;   // WPF: left button
            try { DisarmVoiceMic(); } catch (System.Exception ex) { Serilog.Log.Warning(ex, "MicActivePill_Click failed"); }
        }

    }
}
