using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _launchSettingsApplied;

        internal void ApplyLaunchSettings()
        {
            _launchSettingsApplied = true;
            var enabled = CoreSettings.Current.RunOnStartup;
            if (SessionAutostart.IsRegistered() != enabled)
                SessionAutostart.SetStartupState(enabled);
        }

        private void StartLaunchEngine()
        {
            if (_launchSettingsApplied && CoreSettings.Current.AutoStartEngine)
                StartEngine(systemInitiated: false);
        }
    }
}
