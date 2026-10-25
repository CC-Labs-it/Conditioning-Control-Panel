// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.PlayTab.cs.
//
//   RefreshPlayCards / RefreshPlayFreeStamps / RefreshPlayIntakeCard / SetLockband
//                         PORTED into Views/Tabs/PlayTabView.axaml.cs (the view paints itself on
//                         attach, on IsVisible, on IntakePass.PassStateChanged and from
//                         MainShellWindow.Patreon.cs, which is WPF's three call sites).
//   StartMantraSession    PORTED below. Its one caller is the Programs Mantra-task door.
//   LaunchPlayBreakoutDemo / LaunchPlayChess reuse the launcher's existing GameWindow hosts.
//   LaunchPlayBreakout / LaunchPlayGoon remain unported; their buttons refuse honestly.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.PlayTab.cs: both doors are free and focus an existing game.
        internal void LaunchPlayBreakoutDemo() => Games.GameWindow.Launch("breakoutdemo");

        internal void LaunchPlayChess() => Games.GameWindow.Launch("piecebypiece");

        /// <summary>Builds the Mantra Lab. Tests swap it to make the open fail.</summary>
        internal static System.Func<MantraWindow> NewMantraWindow = () => new MantraWindow();

        /// <summary>
        /// WPF MainWindow.PlayTab.cs StartMantraSession: focus a running MantraWindow rather than
        /// restart it (a second StartSession would wipe the run), else start the session THEN open the
        /// window, whose Loaded reads CurrentMantra and TargetCount. A failure is logged and shown in
        /// WPF's warning text.
        /// </summary>
        internal void StartMantraSession(int targetReps)
        {
            try
            {
                if (global::Avalonia.Application.Current?.ApplicationLifetime is
                        global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    foreach (var w in desktop.Windows)
                        if (w is MantraWindow live) { live.Activate(); live.Focus(); return; }

                App.Mantra.StartSession(targetReps);
                NewMantraWindow().Show(this);
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "StartMantraSession failed");
                // WPF's MessageBox (hardcoded English there too; no loc key exists).
                _ = Dialogs.MessageDialog.ShowAsync(this, "Mantras", "Couldn't start the mantra session:\n\n" + ex.Message);
            }
        }
    }
}
