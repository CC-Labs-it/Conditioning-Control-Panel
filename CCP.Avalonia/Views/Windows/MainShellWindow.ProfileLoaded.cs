using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Autonomy;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private int _profileLoadGeneration;
    private int _profileLoadClosed;

    // WPF MainWindow.xaml.cs:380,704-744: the profile arrives after startup and entitlement.
    private void HookProfileLoaded()
    {
        AccountSeed.ProfileLoaded += OnProfileLoaded;
        Closed += (_, _) =>
        {
            Volatile.Write(ref _profileLoadClosed, 1);
            Interlocked.Increment(ref _profileLoadGeneration);
            AccountSeed.ProfileLoaded -= OnProfileLoaded;
        };
    }

    private void OnProfileLoaded()
    {
        var generation = Volatile.Read(ref _profileLoadGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _profileLoadClosed) != 0) return;
            UpdateLevelDisplay();
            _avatarTubeWindow?.RefreshTitleDisplay();
            App.Descent?.RequestRefresh("profile loaded");
            // Panic holds automatic re-arms, but the adopted profile still needs its repaint.
            if (AutonomyStoppedForPanic || generation != Volatile.Read(ref _profileLoadGeneration)) return;
            var s = CoreSettings.Current;
            if (s.AutonomyResumeOnStartup && s.AutonomyModeEnabled && s.AutonomyConsentGiven
                && !Autonomy.IsEnabled && AutonomyScheduler.HasEntitlement)
                Autonomy.Start();
        });
    }
}
