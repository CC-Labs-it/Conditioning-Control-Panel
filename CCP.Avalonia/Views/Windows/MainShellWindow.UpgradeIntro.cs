using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow/MainWindow.Marquee.cs:296-379: version gate, notes, tour and post-dismissal stamp.
    // WPF Marquee.cs:342 queues the notes at priority 30 on the startup ladder (StartupPresenter.cs:89).
    private bool _upgradeOffered;
    private bool _upgradeCancelled;
    private WhatsNewDialog? _upgradeDialog;
    private TaskCompletionSource? _upgradeTurn;

    private void ArmUpgradeIntro()
    {
        var previous = CoreSettings.Current.LastSeenVersion;
        if (CoreSettings.Service != null && !string.IsNullOrEmpty(previous) && previous != CoreReleaseContent.AppVersion)
            Platform.StartupLadder.ModalPending = true; // hold passive cards until the notes are queued
        Opened += OnUpgradeShellOpened;
        Closed += (_, _) => StopUpgradeIntroForPanic(this);
    }

    internal void OnUpgradeShellOpened(object? sender, EventArgs e)
    {
        Opened -= OnUpgradeShellOpened;
        Dispatcher.UIThread.Post(() => _ = ShowWhatsNewIfNeeded(), DispatcherPriority.Normal);
    }

    /// <summary>Queues the notes; the task completes once they closed, were abandoned or panic cancelled them.</summary>
    internal Task ShowWhatsNewIfNeeded()
    {
        if (_upgradeOffered || _upgradeCancelled || CoreSettings.Service == null) return Task.CompletedTask;
        _upgradeOffered = true;
        var version = CoreReleaseContent.AppVersion;
        var previous = CoreSettings.Current.LastSeenVersion;
        if (previous == version) return Task.CompletedTask;
        var notes = CoreReleaseContent.PatchNotes;
        try
        {
            // WPF Marquee.cs:311: fresh profiles stamp silently. Missing notes use the same fallback.
            if (string.IsNullOrEmpty(previous) || string.IsNullOrWhiteSpace(notes))
            {
                CoreSettings.Current.LastSeenVersion = version;
                CoreSettings.Save();
                return Task.CompletedTask;
            }
            var turn = _upgradeTurn = new TaskCompletionSource();
            // An abandoned turn (five busy minutes) stays unspent, owed next launch.
            Platform.StartupLadder.EnqueueModal(UpgradeModalKey, 30, () => ShowUpgradeNotesAsync(version, notes), () => turn.TrySetResult());
            return turn.Task;
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to queue What's New dialog"); return Task.CompletedTask; }
        finally
        {
            // The queued entry now holds passive offers (StartupLadder.World LadderBusy).
            Platform.StartupLadder.ModalPending = false;
            Platform.StartupLadder.PumpHeld();
        }
    }

    private const string UpgradeModalKey = "whats-new";

    private async Task ShowUpgradeNotesAsync(string version, string notes)
    {
        try
        {
            // Stricter than WPF: never over an unaccepted age gate; the version stays owed.
            if (_upgradeCancelled || !CoreSettings.Current.HasAcceptedAgeVerification) return;
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
        catch (Exception ex) { Log.Warning(ex, "Failed to show What's New dialog"); }
        finally
        {
            _upgradeDialog = null;
            _upgradeTurn?.TrySetResult();
        }
    }

    // Stricter safety: also close/cancel this modal in the WPF MainWindow/MainWindow.xaml.cs:2017 stop-all pass.
    internal static void StopUpgradeIntroForPanic(MainShellWindow? shell)
    {
        var owner = shell ?? Current;
        if (owner != null) owner._upgradeCancelled = true;
        Platform.StartupLadder.CancelModal(UpgradeModalKey);
        owner?._upgradeTurn?.TrySetResult();
        Platform.StartupLadder.CancelDeferred();
        FeatureIntroPopup.CancelPending();
        Platform.StartupLadder.ModalPending = false;
        owner?._upgradeDialog?.Close();
    }
}
