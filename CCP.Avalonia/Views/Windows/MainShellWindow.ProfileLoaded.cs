using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Autonomy;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.xaml.cs:380,704-744: the profile arrives after startup and entitlement.
    private void HookProfileLoaded()
    {
        AccountSeed.ProfileLoaded += OnProfileLoaded;
        Closed += (_, _) => AccountSeed.ProfileLoaded -= OnProfileLoaded;
    }

    private void OnProfileLoaded() => Dispatcher.UIThread.Post(() =>
    {
        UpdateLevelDisplay();
        App.Descent?.RequestRefresh("profile loaded");
        var s = CoreSettings.Current;
        if (s.AutonomyResumeOnStartup && s.AutonomyModeEnabled && s.AutonomyConsentGiven
            && !Autonomy.IsEnabled && AutonomyScheduler.HasEntitlement)
            Autonomy.Start();
    });
}
