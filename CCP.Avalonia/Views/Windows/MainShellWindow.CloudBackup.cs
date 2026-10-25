// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.CloudBackup.cs (285 lines).
// Not "waiting for a service to move to Core" - two structural blockers:
//
// 1. THE BUTTONS LEFT THIS WINDOW. WPF carried the Settings door inline in MainWindow.xaml, so
//    these handlers hung off the shell. The port moved all four WITH their markup to
//    CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml(.cs), where
//    BtnBackupSettingsNow_Click / BtnRestoreSettings_Click / BtnExportData_Click /
//    BtnPrivacyPolicy_Click already exist as that control's own stubs. Restoring them here would
//    be a second copy nothing routes to. Cloud backup gets wired in THAT file.
//
// 2. Backup now / Restore / the status line are wired there (decision 2026-10-22, cloud settings
//    backup: Core CloudSettingsBackup, absolute paths machine-local). Export stays
//    wire-views-settings-account-b-next.
//
// Members, so nothing disappears silently:
//   internal void ReloadSettingsUiAfterRestore()
//       The manual restore repaints through CurrentReplaced + AppSettingsTabView.RefreshSections.
//       The startup restore prompt (App.CheckCloudSettingsRestoreAsync) is not ported.
//   internal async void BtnBackupSettingsNow_Click(…) / BtnRestoreSettings_Click(…)
//       Ported to AccountSettingsSection.BackupNowAsync / RestoreAsync (the local-wins list is
//       CloudSettingsBackup.KeepLocal); the repaint is CurrentReplaced + RefreshSections.
//   internal async void BtnExportData_Click(…)
//       ExportDataAsync, then Microsoft.Win32.SaveFileDialog. Avalonia's twin is
//       StorageProvider.SaveFilePickerAsync on this window; the export it would save is what's missing.
//   internal void BtnPrivacyPolicy_Click(…)
//       Process.Start(UseShellExecute) on the privacy-policy URL. No browser launcher on this head
//       (WPF's is ConditioningControlPanel/Helpers/BrowserLauncher.cs, whose no-default-browser
//       prompt is its whole point). Cheap once one exists - in AccountSettingsSection, per (1).
//   private async Task UpdateBackupStatus()
//       Writes TxtCloudBackupStatus from GetSettingsBackupInfoAsync. Same blocker; that TextBlock
//       is in AccountSettingsSection.axaml too.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose - every member of the WPF twin belongs to
        // CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml.cs on this head.
    }
}
