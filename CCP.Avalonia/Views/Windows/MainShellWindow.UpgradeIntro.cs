using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // ponytail: only What's New needs this modal host today. Reuse Core's readiness rule;
    // a general priority pump belongs with the other startup modal callers when they port.
    private bool _upgradeOffered;
    private bool _upgradeCancelled;
    private WhatsNewDialog? _upgradeDialog;
    internal Func<TimeSpan, Task> UpgradeDelay = Task.Delay;

    private void ArmUpgradeIntro()
    {
        var previous = CoreSettings.Current.LastSeenVersion;
        if (CoreSettings.Service != null && !string.IsNullOrEmpty(previous) && previous != CoreReleaseContent.AppVersion)
            Platform.StartupLadder.ModalPending = true; // hold passive cards before Opened
        Opened += OnUpgradeShellOpened;
        Closed += (_, _) => StopUpgradeIntroForPanic();
    }

    internal void OnUpgradeShellOpened(object? sender, EventArgs e)
    {
        Opened -= OnUpgradeShellOpened;
        Dispatcher.UIThread.Post(async () => await ShowWhatsNewIfNeeded(), DispatcherPriority.Normal);
    }

    internal async Task ShowWhatsNewIfNeeded()
    {
        if (_upgradeOffered || CoreSettings.Service == null) return;
        _upgradeOffered = true;
        var version = CoreReleaseContent.AppVersion;
        var previous = CoreSettings.Current.LastSeenVersion;
        // WPF Marquee.cs:307: a fresh profile is stamped silently, never shown patch notes.
        if (string.IsNullOrEmpty(previous))
        {
            CoreSettings.Current.LastSeenVersion = version;
            CoreSettings.Save();
            return;
        }
        if (previous == version) return;
        Platform.StartupLadder.ModalPending = true;
        try
        {
            // WPF StartupPresenter: 750ms settle, 500ms recheck, five minutes per surface.
            await UpgradeDelay(TimeSpan.FromMilliseconds(750));
            var waited = TimeSpan.Zero;
            while (!_upgradeCancelled && waited < TimeSpan.FromMinutes(5) && !CanShowUpgrade())
            {
                await UpgradeDelay(TimeSpan.FromMilliseconds(500));
                waited += TimeSpan.FromMilliseconds(500);
            }
            if (_upgradeCancelled || !CanShowUpgrade()) return; // unspent, owed next launch
            IsStartupDialogShowing = true;
            Platform.StartupLadder.ModalPending = false;
            try
            {
                _upgradeDialog = new WhatsNewDialog($"What's New in v{version}", CoreReleaseContent.PatchNotes,
                    tourAction: () =>
                    {
                        IsStartupDialogShowing = false;
                        Tours.TutorialHead.StartOnShell(Tours.TutorialType.UpgradeTour);
                    }, tourButtonText: "Show me around (60s)");
                await _upgradeDialog.ShowDialogSafe(this);
                CoreSettings.Current.LastSeenVersion = version;
                CoreSettings.Save();
            }
            finally
            {
                _upgradeDialog = null;
                IsStartupDialogShowing = false;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to show What's New dialog"); }
        finally { Platform.StartupLadder.ModalPending = false; }
    }

    private bool CanShowUpgrade() => StartupQueueCore.CanStartModal(IsStartupDialogShowing,
        Platform.AppUpdater.IsUpdateDialogActive, CoreTutorial.IsActive, IsVisible && CoreSettings.Current.HasAcceptedAgeVerification);

    internal void StopUpgradeIntroForPanic()
    {
        _upgradeCancelled = true;
        Platform.StartupLadder.ModalPending = false;
        _upgradeDialog?.Close();
    }
}
