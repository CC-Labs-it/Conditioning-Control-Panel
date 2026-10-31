using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow/MainWindow.Marquee.cs:296-379: version gate, notes, tour and post-dismissal stamp.
    // WPF Services/Startup/StartupPresenter.cs:42-55, 439-477, 569: timing and modal ownership.
    // Only What's New needs this modal host today. Reuse Core's readiness rule;
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
        Closed += (_, _) => StopUpgradeIntroForPanic(this);
    }

    internal void OnUpgradeShellOpened(object? sender, EventArgs e)
    {
        Opened -= OnUpgradeShellOpened;
        Dispatcher.UIThread.Post(async () => await ShowWhatsNewIfNeeded(), DispatcherPriority.Normal);
    }

    internal async Task ShowWhatsNewIfNeeded()
    {
        if (_upgradeOffered || _upgradeCancelled || CoreSettings.Service == null) return;
        _upgradeOffered = true;
        var version = CoreReleaseContent.AppVersion;
        var previous = CoreSettings.Current.LastSeenVersion;
        if (previous == version) return;
        var notes = CoreReleaseContent.PatchNotes;
        Platform.StartupLadder.ModalPending = true;
        try
        {
            // WPF Marquee.cs:311: fresh profiles stamp silently. Missing notes use the same fallback.
            if (string.IsNullOrEmpty(previous) || string.IsNullOrWhiteSpace(notes))
            {
                CoreSettings.Current.LastSeenVersion = version;
                CoreSettings.Save();
                return;
            }
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
            // WPF StartupPresenter.cs:247,497,554: deferred offers wait until the modal drains.
            try
            {
                _upgradeDialog = new WhatsNewDialog(Loc.GetF("upgrade_notes_title_fmt", version), notes,
                    tourAction: () =>
                    {
                        IsStartupDialogShowing = false;
                        Tours.TutorialHead.StartOnShell(Tours.TutorialType.UpgradeTour);
                    }, tourButtonText: Loc.Get("upgrade_notes_tour_button"));
                await _upgradeDialog.ShowDialogSafe(this);
                if (_upgradeCancelled) return; // panic leaves this version owed next launch
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
        finally
        {
            Platform.StartupLadder.ModalPending = false;
            if (!_upgradeCancelled) Platform.StartupLadder.PumpHeld();
        }
    }

    private bool CanShowUpgrade() => StartupQueueCore.CanStartModal(IsStartupDialogShowing,
        Platform.AppUpdater.IsUpdateDialogActive, CoreTutorial.IsActive, IsVisible && CoreSettings.Current.HasAcceptedAgeVerification);

    // Stricter safety: also close/cancel this modal in the WPF MainWindow/MainWindow.xaml.cs:2017 stop-all pass.
    internal static void StopUpgradeIntroForPanic(MainShellWindow? shell)
    {
        var owner = shell ?? Current;
        if (owner != null) owner._upgradeCancelled = true;
        Platform.StartupLadder.CancelDeferred();
        FeatureIntroPopup.CancelPending();
        Platform.StartupLadder.ModalPending = false;
        owner?._upgradeDialog?.Close();
    }
}
