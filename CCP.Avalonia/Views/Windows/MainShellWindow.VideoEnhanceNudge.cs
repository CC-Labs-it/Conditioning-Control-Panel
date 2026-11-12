// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperTab.cs:373-479
// (MaybePromptMandatoryVideoEnhancement), called from StartEngine as WPF MainWindow.StartStop.cs:443.

using System;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF _mandatoryVideoEnhanceNudgeShown: at most once per launch.
        internal static bool VideoEnhanceNudgeShown;
        internal static Func<string, MandatoryVideoEnhancementScanner.ScanResult> VideoEnhanceScan = MandatoryVideoEnhancementScanner.Scan;
        internal static Func<Window?, string, string, Task<(bool Yes, bool DontAsk)>> VideoEnhanceNudgeDialog = ShowVideoEnhanceNudgeAsync;

        internal static async Task MaybePromptMandatoryVideoEnhancement(Window? owner, Func<bool> engineRunning, Func<bool> webcamRunning, Action startWebcam)
        {
            try
            {
                if (VideoEnhanceNudgeShown) return;
                var s = CoreSettings.Current;
                if (!s.MandatoryVideosEnabled || s.SkipMandatoryVideoEnhanceNudge) return;   // "Don't ask again" (#644)
                if (RemoteControllerConnected || LockdownActive) return;   // never interrupt a remote or a Lockdown
                var folder = System.IO.Path.Combine(CorePaths.EffectiveAssets, "videos");
                var scan = await Task.Run(() => VideoEnhanceScan(folder));
                // The engine may have stopped, or a concurrent start won, while the scan ran.
                if (!engineRunning() || VideoEnhanceNudgeShown || !scan.AnyEnhanced) return;
                var enhanceOff = !s.VideoEnhanceIfPossible;
                var webcamGap = scan.AnyWebcamEnhanced && !webcamRunning();
                if (!enhanceOff && !webcamGap) return;
                VideoEnhanceNudgeShown = true;

                var variant = enhanceOff && webcamGap ? "both" : enhanceOff ? "enhance" : "webcam";
                var body = new StringBuilder(Loc.Get("deeper_nudge_intro")).Append("\n\n");
                if (enhanceOff) body.Append(Loc.Get("deeper_nudge_enhance_off")).Append('\n');
                if (webcamGap) body.Append(Loc.Get("deeper_nudge_webcam_off")).Append('\n');
                body.Append('\n').Append(Loc.Get("deeper_nudge_ask_" + variant));

                await Task.Yield();
                // Re-read at open time, and again for Lockdown: a session that ended is not asked about.
                if (!engineRunning() || LockdownActive) return;
                var (yes, dontAsk) = await VideoEnhanceNudgeDialog(owner, body.ToString(), Loc.Get("deeper_nudge_yes_" + variant));
                // #644: the box stops the question on either button; it never flips a setting by itself.
                if (dontAsk) s.SkipMandatoryVideoEnhanceNudge = true;
                if (yes && enhanceOff)
                {
                    s.VideoEnhanceIfPossible = true;
                    Serilog.Log.Information("Mandatory-video enhancement enabled via engine-start nudge.");
                }
                if (dontAsk || (yes && enhanceOff)) CoreSettings.Save();
                // The manual toggle's flow, so consent and calibration gating are identical.
                if (yes && webcamGap) startWebcam();
            }
            catch (Exception ex) { Serilog.Log.Debug("MaybePromptMandatoryVideoEnhancement: {Error}", ex.Message); }
        }

        private static async Task<(bool, bool)> ShowVideoEnhanceNudgeAsync(Window? owner, string body, string yes)
        {
            if (owner == null) return (false, false);
            var dialog = new MessageDialog(Loc.Get("deeper_nudge_title"), body, showCancel: true,
                okText: yes, cancelText: Loc.Get("companion_ask_not_now"));
            var dontAsk = new CheckBox { Content = new TextBlock { Text = Loc.Get("chk_dont_ask_again") }, VerticalAlignment = VerticalAlignment.Center };
            ((Panel)dialog.FindControl<Button>("BtnOk")!.Parent!).Children.Add(dontAsk);   // footer column 0
            var accepted = await dialog.ShowDialogSafe<bool?>(owner) == true;
            return (accepted, dontAsk.IsChecked == true);
        }
    }
}
