// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperTab.cs:373-479
// (MaybePromptMandatoryVideoEnhancement), called from StartEngine as WPF MainWindow.StartStop.cs:443.

using System;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF _mandatoryVideoEnhanceNudgeShown: at most once per launch.
        internal static bool VideoEnhanceNudgeShown;
        internal static Func<string, MandatoryVideoEnhancementScanner.ScanResult> VideoEnhanceScan = MandatoryVideoEnhancementScanner.Scan;
        internal static Action<string, int, Func<Task>> QueueVideoNudge = (key, priority, show) => StartupLadder.EnqueueModal(key, priority, show);
        private static int _nudgeGeneration;
        private static MessageDialog? _nudgeDialog;
        internal static Func<Window?, string, string, Task<(bool Yes, bool DontAsk)>> VideoEnhanceNudgeDialog = ShowVideoEnhanceNudgeAsync;

        internal static async Task MaybePromptMandatoryVideoEnhancement(Window? owner, Func<bool> engineRunning, Func<bool> webcamRunning, Action startWebcam)
        {
            try
            {
                if (VideoEnhanceNudgeShown) return;
                var s = CoreSettings.Current;
                if (!s.MandatoryVideosEnabled || s.SkipMandatoryVideoEnhanceNudge) return;   // "Don't ask again" (#644)
                if (RemoteControllerConnected || LockdownActive) return;   // never interrupt a remote or a Lockdown
                var generation = _nudgeGeneration;
                var folder = System.IO.Path.Combine(CorePaths.EffectiveAssets, "videos");
                var scan = await Task.Run(() => VideoEnhanceScan(folder));
                // The engine may have stopped, or a concurrent start won, while the scan ran.
                if (generation != _nudgeGeneration || !engineRunning() || VideoEnhanceNudgeShown || !scan.AnyEnhanced) return;
                var enhanceOff = !s.VideoEnhanceIfPossible;
                var webcamGap = scan.AnyWebcamEnhanced && !webcamRunning();
                if (!enhanceOff && !webcamGap) return;
                VideoEnhanceNudgeShown = true;

                var variant = enhanceOff && webcamGap ? "both" : enhanceOff ? "enhance" : "webcam";
                var body = new StringBuilder(Loc.Get("deeper_nudge_intro")).Append("\n\n");
                if (enhanceOff) body.Append(Loc.Get("deeper_nudge_enhance_off")).Append('\n');
                if (webcamGap) body.Append(Loc.Get("deeper_nudge_webcam_off")).Append('\n');
                body.Append('\n').Append(Loc.Get("deeper_nudge_ask_" + variant));

                QueueVideoNudge("deeper-enhance-nudge", 70, async () =>
                {
                    if (generation != _nudgeGeneration || !engineRunning() || LockdownActive || RemoteControllerConnected) return;
                    var (yes, dontAsk) = await VideoEnhanceNudgeDialog(owner, body.ToString(), Loc.Get("deeper_nudge_yes_" + variant));
                    if (generation != _nudgeGeneration || !engineRunning() || LockdownActive || RemoteControllerConnected) return;
                    if (dontAsk) s.SkipMandatoryVideoEnhanceNudge = true;
                    if (yes && enhanceOff)
                    {
                        s.VideoEnhanceIfPossible = true;
                        Serilog.Log.Information("Mandatory-video enhancement enabled via engine-start nudge.");
                    }
                    if (dontAsk || (yes && enhanceOff)) CoreSettings.Save();
                    if (yes && webcamGap) startWebcam();
                });
            }
            catch (Exception ex) { Serilog.Log.Debug("MaybePromptMandatoryVideoEnhancement: {Error}", ex.Message); }
        }

        internal static void StopVideoEnhanceNudgeForPanic(MainShellWindow? shell)
        {
            _nudgeGeneration++;
            _nudgeDialog?.Close();
        }

        private static async Task<(bool, bool)> ShowVideoEnhanceNudgeAsync(Window? owner, string body, string yes)
        {
            if (owner == null) return (false, false);
            var dialog = _nudgeDialog = new MessageDialog(Loc.Get("deeper_nudge_title"), body, showCancel: true,
                okText: yes, cancelText: Loc.Get("companion_ask_not_now"));
            var dontAsk = new CheckBox { Content = new TextBlock { Text = Loc.Get("chk_dont_ask_again") }, VerticalAlignment = VerticalAlignment.Center };
            ((Panel)dialog.FindControl<Button>("BtnOk")!.Parent!).Children.Add(dontAsk);   // footer column 0
            try
            {
                var accepted = await dialog.ShowDialogSafe<bool?>(owner) == true;
                return (accepted, dontAsk.IsChecked == true);
            }
            finally { _nudgeDialog = null; }
        }
    }
}
