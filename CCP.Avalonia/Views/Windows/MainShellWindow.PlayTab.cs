// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.PlayTab.cs.
//
//   RefreshPlayCards / RefreshPlayFreeStamps / RefreshPlayIntakeCard / SetLockband
//                         PORTED into Views/Tabs/PlayTabView.axaml.cs (the view paints itself on
//                         attach, on IsVisible, on IntakePass.PassStateChanged and from
//                         MainShellWindow.Patreon.cs, which is WPF's three call sites).
//   StartMantraSession    PORTED below. Its one caller is the Programs Mantra-task door.
//   LaunchPlayBreakoutDemo / LaunchPlayChess reuse the launcher's existing GameWindow hosts.
//   LaunchPlayLauncherGame runs the launcher's Destinations (Racing Thoughts); OpenPlayWebApp is
//                         in MainShellWindow.TabNavigation.cs beside the rail door it shares.
//   LaunchPlayBreakout / LaunchPlayGoon / LaunchPlayBackRoom and the dtrh/arcademy entries remain
//                         unported (no host on this head); their buttons refuse honestly.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.PlayTab.cs: both doors are free and focus an existing game.
        internal void LaunchPlayBreakoutDemo() => Games.GameWindow.Launch("breakoutdemo");

        internal void LaunchPlayChess() => Games.GameWindow.Launch("piecebypiece");

        /// <summary>WPF LaunchPlayLauncherGame -> LaunchExclusiveGame (MainWindow.Exclusives.cs:247): the
        /// launcher's own entry, so its account rule and gate answer. Signed out -> the login dialog; an id
        /// this head has no destination for (dtrh, arcademy) does nothing, as WPF's unavailable entry.</summary>
        internal void LaunchPlayLauncherGame(string id)
        {
            if (global::ConditioningControlPanel.Services.Launcher.LauncherCards.Find(id) is not { } card
                || !LauncherWindow.Destinations.TryGetValue(id, out var dest)) return;
            if (card.RequiresAccount && !CoreAccount.IsLoggedIn) { _ = OpenUnifiedLoginDialog(); return; }
            dest.Open(this);
        }

        /// <summary>
        /// WPF MainWindow.PlayTab.cs StartMantraSession: focus a running MantraWindow rather than
        /// restart it (a second StartSession would wipe the run), else start the session THEN open the
        /// window, whose Loaded reads CurrentMantra and TargetCount.
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
                new MantraWindow().Show(this);
            }
            catch (System.Exception ex)
            {
                // ponytail: WPF also shows a MessageBox here; logged only on this head.
                Serilog.Log.Error(ex, "StartMantraSession failed");
            }
        }
    }
}
