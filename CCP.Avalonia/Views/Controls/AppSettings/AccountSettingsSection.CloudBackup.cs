using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    public partial class AccountSettingsSection
    {
        internal static Func<CloudSettingsBackup?> NewBackupClient =
            () => AccountSeed.Patreon == null ? null : new CloudSettingsBackup();

        private bool _backupBusy;

        private void BtnBackupSettingsNow_Click(object? sender, RoutedEventArgs e) => _ = BackupNowAsync();
        private void BtnRestoreSettings_Click(object? sender, RoutedEventArgs e) => _ = RestoreFromCloudAsync();

        // SetCurrentValue keeps the markup's {loc:Str} binding alive under the busy text.
        private void SetButtonText(string name, string key)
        {
            if (Find<Button>(name)?.Content is IconLabel icon) icon.Key = key;
            else if (Find<Button>(name)?.Content is TextBlock text)
                text.SetCurrentValue(TextBlock.TextProperty, Loc.Get(key));
        }

        internal async Task BackupNowAsync()
        {
            if (_backupBusy || NewBackupClient() is not { } client) return;
            var btn = Find<Button>("BtnBackupSettingsNow");
            _backupBusy = true;
            if (btn != null) btn.IsEnabled = false;
            SetButtonText("BtnBackupSettingsNow", "btn_backing_up");
            try
            {
                var ok = await client.BackupAsync();
                if ((TopLevel.GetTopLevel(this) as Window) is { } owner)
                    await MessageDialog.ShowAsync(owner,
                        Loc.Get(ok ? "title_backup_complete" : "title_backup_failed"),
                        Loc.Get(ok ? "msg_settings_backed_up_to_cloud_successfully" : "msg_failed_to_backup_settings"));
                if (ok) await UpdateBackupStatusAsync(client);
            }
            catch (Exception ex)
            {
                Log.Warning("Manual settings backup failed: {E}", ex.Message);
                if (TopLevel.GetTopLevel(this) is Window owner)
                    await MessageDialog.ShowAsync(owner, Loc.Get("title_backup_error"), Loc.GetF("msg_backup_failed_0", ex.Message));
            }
            finally
            {
                _backupBusy = false;
                if (btn != null) btn.IsEnabled = true;
                SetButtonText("BtnBackupSettingsNow", "btn_backup_now");
            }
        }

        internal async Task RestoreFromCloudAsync()
        {
            if (_backupBusy || NewBackupClient() is not { } client) return;
            // RestoreFrom swaps the whole settings object, so mid-session every prescribed value is
            // gone at once: refused exactly as WPF refuses it ("cloud-restore-settings").
            if (Shell?.RefuseActionIfSessionLocked("cloud-restore-settings") ?? CoreSession.IsSessionRunning) return;
            var owner = (TopLevel.GetTopLevel(this) as Window);
            if (owner != null && !await MessageDialog.ConfirmAsync(owner,
                    Loc.Get("title_restore_settings_from_cloud"), Loc.Get("msg_restore_settings_confirm"), defaultToCancel: true))
                return;

            var btn = Find<Button>("BtnRestoreSettings");
            _backupBusy = true;
            if (btn != null) btn.IsEnabled = false;
            SetButtonText("BtnRestoreSettings", "btn_restoring");
            try
            {
                var restored = await client.DownloadAsync();
                // A session may have started while the dialog or the download was up: say so, not "no backup".
                if (CoreSession.IsSessionRunning)
                {
                    if (Shell?.RefuseActionIfSessionLocked("cloud-restore-settings") != true && owner != null)
                        await MessageDialog.ShowAsync(owner, Loc.Get("session_lock_action_title"),
                            Loc.GetF("session_lock_action_body", Loc.Get("session_lock_reason")));
                    return;
                }
                if (restored == null || CoreSettings.Service is not { } service)
                {
                    if (owner != null)
                        await MessageDialog.ShowAsync(owner, Loc.Get("title_restore_failed"), Loc.Get("msg_no_cloud_backup_found_or_restore_failed"));
                    return;
                }
                CloudSettingsBackup.ApplyLocalWins(service.Current, restored);
                var kept = CloudSettingsBackup.KeepLocalMissingPaths(service.Current, restored);
                service.RestoreFrom(restored);   // raises CurrentReplaced: every page re-seeds from it
                this.FindAncestorOfType<Tabs.AppSettingsTabView>()?.RefreshSections();
                Refresh();
                if (owner != null)
                    await MessageDialog.ShowAsync(owner, Loc.Get("title_settings_restored"), Loc.Get("msg_settings_restored_from_cloud")
                        + (kept.Count == 0 ? "" : "\n\n" + Loc.GetF("msg_restore_kept_local_paths_0", string.Join(", ", kept))));
            }
            catch (Exception ex)
            {
                Log.Warning("Manual settings restore failed: {E}", ex.Message);
                if (owner != null)
                    await MessageDialog.ShowAsync(owner, Loc.Get("title_restore_error"), Loc.GetF("msg_restore_failed_0", ex.Message));
            }
            finally
            {
                _backupBusy = false;
                if (btn != null) btn.IsEnabled = true;
                SetButtonText("BtnRestoreSettings", "btn_restore_from_cloud");
            }
        }

        internal async Task UpdateBackupStatusAsync(CloudSettingsBackup? client = null)
        {
            var line = Find<TextBlock>("TxtCloudBackupStatus");
            if (line == null || string.IsNullOrEmpty(CoreSettings.Current.UnifiedId)) return;
            client ??= NewBackupClient();
            if (client == null) return;
            try
            {
                var info = await client.GetInfoAsync();
                line.Text = info?.BackedUpAt is { } at
                    ? Loc.GetF("label_last_backup_0_v_1", at.ToLocalTime().ToString("MMM d, yyyy h:mm tt"), info.AppVersion)
                    : Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to update backup status: {E}", ex.Message);
                line.Text = Loc.Get("label_could_not_check_backup_status");
            }
        }
    }
}
