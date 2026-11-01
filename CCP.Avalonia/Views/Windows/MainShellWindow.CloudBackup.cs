// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.CloudBackup.cs (285 lines).
// Not "waiting for a service to move to Core" - two structural blockers:
//
// 1. THE BUTTONS LEFT THIS WINDOW. WPF carried the Settings door inline in MainWindow.xaml, so
//    these handlers hung off the shell. The port moved all four WITH their markup to
//    CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml(.cs), where
//    BtnBackupSettingsNow_Click / BtnRestoreSettings_Click / BtnExportData_Click /
//    BtnPrivacyPolicy_Click live in that control. Restoring them here would
//    be a second copy nothing routes to. Cloud backup gets wired in THAT file.
//
// 2. Backup now / Restore / the status line are wired there (decision 2026-10-22, cloud settings
//    backup: Core CloudSettingsBackup, absolute paths machine-local). Export is also wired there.
//
// Members, so nothing disappears silently:
//   internal void ReloadSettingsUiAfterRestore()
//       The manual restore repaints through CurrentReplaced + AppSettingsTabView.RefreshSections.
//       The startup restore prompt (App.CheckCloudSettingsRestoreAsync) is not ported.
//   internal async void BtnBackupSettingsNow_Click(…) / BtnRestoreSettings_Click(…)
//       Ported to AccountSettingsSection.BackupNowAsync / RestoreAsync (the local-wins list is
//       CloudSettingsBackup.KeepLocal); the repaint is CurrentReplaced + RefreshSections.
//   internal async void BtnExportData_Click(…)
//       ExportDataAsync and the save picker live in AccountSettingsSection.
//   internal void BtnPrivacyPolicy_Click(…)
//       Opens the privacy-policy URL from AccountSettingsSection.
//   private async Task UpdateBackupStatus()
//       Writes TxtCloudBackupStatus from GetInfoAsync in AccountSettingsSection.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose - every member of the WPF twin belongs to
        // CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml.cs on this head.
    }
}
