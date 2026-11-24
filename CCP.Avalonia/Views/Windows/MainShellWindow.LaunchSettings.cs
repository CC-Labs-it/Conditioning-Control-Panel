using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _launchSettingsApplied;

        internal static MainShellWindow CreateForLaunch(SessionManager? sessions = null)
        {
            // General settings reads the registration during XAML construction.
            var enabled = CoreSettings.Current.RunOnStartup;
            if (SessionAutostart.IsRegistered() != enabled)
                SessionAutostart.SetStartupState(enabled);
            var shell = sessions is null ? new MainShellWindow() : new MainShellWindow(sessions);
            shell._launchSettingsApplied = true;
            return shell;
        }

        private void StartLaunchEngine()
        {
            if (_launchSettingsApplied && CoreSettings.Current.AutoStartEngine)
                StartEngine(systemInitiated: false);
        }
    }
}
