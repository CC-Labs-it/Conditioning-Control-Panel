// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.BlinkTrainer.cs.
//
// The camera-free half now lives in the page itself, CCP.Avalonia/Views/Tabs/BlinkTrainerTabView
// .axaml.cs: tab-show refresh with an _loading guard (settings load pass), demo stage loop (frames
// moved to /Assets/BlinkTrainer/Demo and linked by both heads), premium gate, status row (decided by
// CCP.Core/Services/BlinkTrainerState.cs, which WPF also calls), folder library (StorageProvider),
// session settings editors, opacity fill and slider label polish. The Play wall's Blink Trainer card
// routes here (PlayTabView BtnLabBlinkTrainerOpenNew_Click).
//
// Live on the page: the tracker toggle (Platform/WebcamTracker), session start/stop with its countdown
// (Views/Overlays/BlinkTrainerSession: click-through overlays per screen, swap per blink, duration
// auto-stop, haptic pulse, quest credit; panic and exit stop it), the live stage preview (images and,
// through a VlcFrameSink, muted looping video), consent manage/revoke (WebcamTracker.RevokeConsent,
// the seeded CoreWebcam verb), Quick Recal over the gaze feed, the 16-point calibration
// (WebcamCalibrationWindow), the help popover, and the status pulse (the page calls
// SetBlinkTrainerStatusPulse in MainShellWindow.TabFxTakeoverLabStatus.cs). STILL MISSING:
// GIF/animated webp animation (first frame only), the SeasonRecap credit (WPF
// BlinkTrainerService.cs:177). Camera shortcuts live in MainShellWindow.CameraShortcut.cs;
// tracking-monitor selection uses Platform.WebcamScreens. Status presentation follows WPF
// UpdateWebcamStatusChips (MainWindow.LabTab.cs:356). The loading splash is live in
// MainShellWindow.WebcamSplash.cs; the Deeper hub webcam twins now relay to the same Blink Trainer
// actions and refresh consent/calibration/tracker state on both cards.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // The camera actions are shared by the two page launchers; see the header.
    }
}
