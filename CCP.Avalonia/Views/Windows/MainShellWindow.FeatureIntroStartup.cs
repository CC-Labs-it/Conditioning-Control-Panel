using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private WhatsNewDialog? _startupWhatsNew;

    /// <summary>WPF MainWindow.Marquee.ShowWhatsNewIfNeeded: fresh installs stamp silently; upgrades get priority 30.</summary>
    internal void ShowWhatsNewIfNeeded()
    {
        if (CoreSettings.Service == null) return;
        var settings = CoreSettings.Current;
        var version = CoreReleaseContent.AppVersion;
        var last = settings.LastSeenVersion ?? "";
        if (last == version) return;
        if (string.IsNullOrEmpty(last))
        {
            settings.LastSeenVersion = version;
            CoreSettings.Save();
            return;
        }
        // ponytail: EmiDeskService's afterUpdate moment bus is unported; no replacement notification.
        StartupLadder.EnqueueModal("whats-new", 30, this, async () =>
        {
            _startupWhatsNew = new WhatsNewDialog($"What's New in v{version}", CoreReleaseContent.PatchNotes,
                tourAction: () => { IsStartupDialogShowing = false; CoreTutorial.Start("UpgradeTour"); },
                tourButtonText: "Show me around (60s)");
            try
            {
                await _startupWhatsNew.ShowDialogSafe(this);
                CoreSettings.Current.LastSeenVersion = version;
                CoreSettings.Save();
            }
            finally { _startupWhatsNew = null; }
        });
    }

    internal void StopStartupModalsForPanic()
    {
        StartupLadder.CancelModals();
        _startupWhatsNew?.Close();
    }
}
