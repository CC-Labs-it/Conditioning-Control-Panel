// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs (1413 lines) - the
// webcam and microphone partial. Sorted member by member, and unlike its neighbours the blanket
// claim holds here: this file is the camera and the mic. What it does NOT hold for:
//
//   OpenDeviceSettings and RefreshDeviceSettingsLists are listed below as dropped, and one of them
//   is stale. OpenDeviceSettings ALREADY SHIPS on this head - MainShellWindow.Settings.cs:82,
//   ShowTab("appsettings") + AppSettingsPage.FocusSection("devices"), asserted by --nav-check and
//   called from AssetsTabView (Library > Folders). Do not re-add it here; a second definition is a compile
//   error, and a second copy would be the wrong kind of fix if it were not.
//   RefreshDeviceSettingsLists genuinely is missing, and needs the camera/monitor enumeration
//   below before it means anything.
//
// The WEBCAM pill is PORTED (MainShellWindow.SafetyPills.cs: the click stops the Blink Trainer and
// the tracker), and so is the 6-blink stop gesture (MainShellWindow.BlinkStop.cs). The MIC pill is
// wired (MainShellWindow.SheListening.cs): its click is DisarmVoiceMic, which really closes the capture.
//
// The rest is genuinely the device: WebcamTrackingService and its debug counters, the calibration
// and quick-recal flows, GazeSide/FocusGaze, the blink trainer's countdown,
// the camera and monitor enumerations, and the two consent buttons (WebcamConsent itself IS in
// Core, but "revoke" has to stop a running capture, which is App.Webcam again).
//
// Members dropped (of WPF's 69; entries marked PORTED have since been ported; OpenDeviceSettings is already live elsewhere, see above):
//   private bool _webcamDebugSubscribed
//   private int _webcamDebugBlinkCount
//   private int _webcamDebugMouthOpenCount
//   private int _webcamDebugTongueOutCount
//   private GazeSide _webcamDebugLastGaze
//   private bool _webcamDebugLastGazeSet
//   private string _webcamDebugFaceLabel
//   private Action<WebcamTrackingState>? _onDebugStateChanged
//   private Action? _onDebugFaceFound
//   private Action? _onDebugFaceLost
//   private Action? _onDebugBlink
//   private Action? _onDebugMouthOpen
//   private Action? _onDebugTongueOut
//   private Action<GazeSide>? _onDebugGazeSide
//   private Action<WebcamTrackingState>? _onPillStateChanged    (PORTED: SafetyPills.cs)
//   (WireMicActivePill / UpdateMicPill / MicActivePill_Click: PORTED, MainShellWindow.SheListening.cs)
//   (WireWebcamActivePill / WebcamActivePill_Click: PORTED, MainShellWindow.SafetyPills.cs)
//   (RapidBlinkRecal* / WireRapidBlinkRecalibrateShortcut / TriggerRapidBlinkRecalibrateAsync /
//    StopAllForRecalibration: PORTED, MainShellWindow.BlinkStop.cs)
//   private bool _syncingBlinkRecalToggles
//   internal void ChkBlinkRecalShortcut_Changed(…)
//   private void SyncBlinkRecalToggles(…)
//   (UpdateLabTrackerUi / UpdateWebcamStatusChips: PORTED, PlayTabView/BlinkTrainerTabView/DeeperTabView/
//    DevicesSettingsSection via WebcamStatusPresentation; WebcamStateText: PORTED, Core WebcamStatus.TextKey)
//   internal void OpenDeviceSettings(…)               ALREADY LIVE (MainShellWindow.Settings.cs:82)
//   internal void RefreshDeviceSettingsLists(…)
//   private string? _quickRecalTooltipBase
//   private void RefreshQuickRecalHotkeyHint(…)
//   internal async void BtnWebcamDebugStart_Click(…)
//   private async Task<bool> StartWebcamOffUiThreadAsync(…)
//   private void InstallWebcamLoadingSplash(…)          PORTED: MainShellWindow.WebcamSplash.cs
//   private void EnsureWebcamDebugSubscribed(…)
//   private void UnsubscribeWebcamDebug(…)
//   private void UpdateWebcamDebugCounters(…)
//   internal async void BtnWebcamDebugCalibrate_Click(…)
//   internal void BtnGazeMinigame_Click(…)
//   (_focusGazeSyncing / HookFocusGazeService / SyncFocusGazeToggle / ChkFocusGaze_Changed: PORTED, InitializeFocusGaze
//    below + PlayTabView; the engine is Platform/GazeFocus.cs, bubbles only so far)
//   private DispatcherTimer? _blinkTrainerTickTimer
//   private void HookBlinkTrainerService(…)
//   private void OnBlinkTrainerServiceStateChanged(…)
//   private void SyncBlinkTrainerCountdownTimer(…)
//   private void BlinkTrainerTick(…)
//   internal void BtnLabBlinkTrainerOpenNew_Click(…)
//   internal async void BtnWebcamDebugTrackerTest_Click(…)
//   internal async void BtnWebcamDebugQuickRecal_Click(…)
//   internal void BtnWebcamReviewPrivacy_Click(…)
//   internal void BtnWebcamRevokeConsent_Click(…)
//   internal void ChkWebcamDebugCursor_Changed(…)
//   internal void ChkWebcamDriftCorrection_Changed(…)
//   internal void ChkRestrictGazeToCalScreen_Changed(…)
//   private bool _webcamDevicePopulating
//   private void PopulateWebcamDeviceCombos(…)
//   private static void PopulateWebcamCombo(…)
//   private void RefreshWebcamDeviceList(…)
//   internal void CmbWebcamDevice_SelectionChanged(…)
//   internal void BtnWebcamDeviceRefresh_Click(…)
//   private bool _webcamMonitorPopulating
//   private void RefreshWebcamMonitorList(…)
//   private static void FillMonitorCombo(…)
//   internal void CmbWebcamMonitor_SelectionChanged(…)
//   private void AppendWebcamDebugLog(…)

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
