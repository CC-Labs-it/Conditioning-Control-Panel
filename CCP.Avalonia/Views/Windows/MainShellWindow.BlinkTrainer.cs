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
// ToggleWebcamFromHotkey (global hotkey) and the explicit tracking-monitor pick (follow-up
// wire-shell-blink-trainer-b-next). Ported below: UpdateWebcamStatusChips (MainWindow.LabTab.cs:356),
// the read-only chip on Play, Blink Trainer and Deeper. The loading splash is live in
// MainShellWindow.WebcamSplash.cs; the Deeper hub webcam twins now relay to the same Blink Trainer
// actions and refresh consent/calibration/tracker state on both cards.

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF WebcamStateText (MainWindow.LabTab.cs:389) over what this head's tracker
        /// reports: running, starting, a failed start, or stopped. It has no FaceLost / CameraInUse
        /// split, so a failed start is the generic error line.</summary>
        internal static string WebcamStateKey(WebcamTracker t) =>
            t.IsRunning ? "rf_webcam_tracking"
            : t.IsStarting ? "rf_webcam_starting"
            : !string.IsNullOrEmpty(t.LastError) ? "rf_webcam_error"
            : "rf_webcam_stopped";

        /// <summary>WPF UpdateWebcamStatusChips (MainWindow.LabTab.cs:356): green dot while the
        /// tracker runs, muted otherwise, and the state line. Display only. Each page calls it with its
        /// own two parts on show and on every tracker state change. The text stays a {loc} binding so
        /// a language switch re-renders it.</summary>
        internal static void PaintWebcamStatusChip(Ellipse dot, TextBlock label)
        {
            bool live = WebcamTracker.Instance.IsRunning;
            dot.Fill = live
                ? new SolidColorBrush(dot.TryFindResource("SuccessGreen", out var g) && g is Color c ? c : Color.FromRgb(0x3E, 0xE8, 0x7A))
                : dot.TryFindResource("TextMutedBrush", out var m) && m is IBrush muted ? muted : Brushes.Gray;
            label.Bind(TextBlock.TextProperty, (Binding)new StrExtension(WebcamStateKey(WebcamTracker.Instance)).ProvideValue(null!));
        }
    }
}
