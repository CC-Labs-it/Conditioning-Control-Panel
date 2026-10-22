// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.CloudBackup.cs (285 lines), into
// CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml(.cs), where the buttons moved with their
// markup (WPF carried the Settings door inline in MainWindow.xaml, so its handlers hung off the shell):
//   UpdateBackupStatus        -> AccountSettingsSection.UpdateBackupStatusAsync (Core V2AuthService.GetSettingsBackupInfoAsync)
//   BtnExportData_Click       -> AccountSettingsSection.ExportDataAsync (Core V2AuthService.ExportDataAsync + save picker)
//   BtnPrivacyPolicy_Click    -> AccountSettingsSection.BtnPrivacyPolicy_Click (ExternalOpener)
// NOT PORTED, by decision (docs/avalonia-decisions.md 2026-10-09 rows-cloud-remember): BtnBackupSettingsNow_Click,
// BtnRestoreSettings_Click and ReloadSettingsUiAfterRestore. Uploading Linux settings and restoring Windows backups here
// (and the reverse) needs a path-translation design for CustomAssetsPath / the asset lists first; the two buttons stay
// hidden. Follow-up: parity row views-settings-account, lane wire-views-settings-account-b-next.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose - every member of the WPF twin belongs to
        // CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml.cs on this head.
    }
}
