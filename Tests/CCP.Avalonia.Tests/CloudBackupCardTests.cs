using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]   // swaps CoreSettings.ServiceProvider, the session probe and the client seam
public sealed class CloudBackupCardTests
{
    private sealed class Wire : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public string Backup = "null";
        public Task? Delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Paths.Add(r.RequestUri!.AbsolutePath);
            if (Delay != null) await Delay;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"backup\":" + Backup + "}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string Pack(JObject o)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None));
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    private static async Task<MessageDialog> Dialog(Window shell, MessageDialog? not = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (shell.OwnedWindows.OfType<MessageDialog>().FirstOrDefault(d => d != not && d.IsVisible) is { } hit) return hit;
            await Task.Yield();
        }
        throw new TimeoutException("dialog never appeared");
    }

    private static string? Title(MessageDialog d) => d.Title;

    private static void Click(Window dialog, string name) =>
        dialog.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task TheCard_FollowsTheAccount_BacksUp_AndRestoresWithoutLooseningSafety() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        var (oldProvider, oldClient, oldSession) =
            (CoreSettings.ServiceProvider, AccountSettingsSection.NewBackupClient, CoreSession.IsSessionRunningProvider);
        var service = new SettingsService();
        var original = service.Current;
        var (oldId, oldStrict, oldPanic) = (original.UnifiedId, original.StrictLockEnabled, original.PanicKeyEnabled);
        var wire = new Wire();
        MainShellWindow? shell = null;
        try
        {
            // The shipped client exists exactly when the Patreon provider (its token source) does.
            Assert.Equal(AccountSeed.Patreon != null, AccountSettingsSection.NewBackupClient() != null);

            CoreSettings.ServiceProvider = () => service;
            CoreSession.IsSessionRunningProvider = () => false;
            AccountSettingsSection.NewBackupClient =
                () => new CloudSettingsBackup(() => CoreSettings.Current, wire, () => "tok");

            original.UnifiedId = null;
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            var section = shell.Named<AppSettingsTabView>("AppSettingsTab")!.FindControl<AccountSettingsSection>("SectionAccount")!;
            section.Refresh();
            var card = section.FindControl<Border>("CloudSettingsBackupSection")!;
            Assert.False(card.IsVisible);                                     // WPF signed-out state: shut

            original.UnifiedId = "u-1";
            original.StrictLockEnabled = false;
            original.PanicKeyEnabled = true;
            section.Refresh();
            Assert.True(card.IsVisible);

            var status = section.FindControl<TextBlock>("TxtCloudBackupStatus")!;
            await section.UpdateBackupStatusAsync();
            Assert.Equal(Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t"), status.Text);
            wire.Backup = "{\"app_version\":\"7.1.5\",\"backed_up_at\":\"2026-10-09T18:30:00Z\",\"size_bytes\":10}";
            await section.UpdateBackupStatusAsync();
            Assert.Contains("7.1.5", status.Text);

            // Backup now: busy label while the upload runs, then WPF's result dialog.
            wire.Paths.Clear();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.Delay = gate.Task;
            var pending = section.BackupNowAsync();
            var backupBtn = section.FindControl<Button>("BtnBackupSettingsNow")!;
            Assert.False(backupBtn.IsEnabled);
            Assert.Equal("btn_backing_up", Assert.IsType<IconLabel>(backupBtn.Content).Key);
            gate.SetResult();
            var done = await Dialog(shell);
            Assert.Equal(Loc.Get("title_backup_complete"), Title(done));
            done.Close();
            await pending;
            wire.Delay = null;
            Assert.Equal("/v2/user/backup-settings", wire.Paths[0]);
            Assert.True(backupBtn.IsEnabled);
            Assert.Equal("btn_backup_now", Assert.IsType<IconLabel>(backupBtn.Content).Key);

            var evil = JObject.FromObject(new AppSettings());
            evil["FlashFrequency"] = 9;
            evil["StrictLockEnabled"] = true;
            evil["PanicKeyEnabled"] = false;
            evil["UnifiedId"] = "someone-else";
            evil["SpiralPath"] = @"C:\Users\bambi\spiral.gif";   // another machine's path: the local one stays
            wire.Backup = "{\"app_version\":\"7.1.5\",\"settings_data\":\"" + Pack(evil) + "\"}";

            // A running session: the shell refuses and explains; nothing is fetched.
            wire.Paths.Clear();
            CoreSession.IsSessionRunningProvider = () => true;
            pending = section.RestoreFromCloudAsync();   // not awaited first: a broken refusal would wait on the confirm forever
            var refused = await Dialog(shell);
            Assert.Equal(Loc.Get("session_lock_action_title"), Title(refused));
            refused.Close();
            await pending;
            Assert.Empty(wire.Paths);
            Assert.Same(original, service.Current);

            // Confirm cancelled: nothing is fetched.
            CoreSession.IsSessionRunningProvider = () => false;
            pending = section.RestoreFromCloudAsync();
            var ask = await Dialog(shell);
            Assert.Equal(Loc.Get("title_restore_settings_from_cloud"), Title(ask));
            Click(ask, "BtnCancel");
            await pending;
            Assert.Empty(wire.Paths);
            Assert.Same(original, service.Current);

            // A session starts during the download: refused with the session message, not "no backup".
            gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.Delay = gate.Task;
            pending = section.RestoreFromCloudAsync();
            ask = await Dialog(shell);
            Click(ask, "BtnOk");
            CoreSession.IsSessionRunningProvider = () => true;
            gate.SetResult();
            refused = await Dialog(shell, ask);
            Assert.Equal(Loc.Get("session_lock_action_title"), Title(refused));
            refused.Close();
            await pending;
            wire.Delay = null;
            Assert.Same(original, service.Current);

            // Confirmed with no session: restored, keeping identity and the safety floor.
            CoreSession.IsSessionRunningProvider = () => false;
            wire.Paths.Clear();
            pending = section.RestoreFromCloudAsync();
            ask = await Dialog(shell);
            Click(ask, "BtnOk");
            done = await Dialog(shell, ask);
            Assert.Equal(Loc.Get("title_settings_restored"), Title(done));
            Assert.EndsWith("\n\n" + Loc.GetF("msg_restore_kept_local_paths_0", Loc.Get("label_spiral")),
                done.FindControl<TextBlock>("TxtMessage")!.Text);
            done.Close();
            await pending;
            Assert.Equal("/v2/user/settings-backup", wire.Paths[0]);
            Assert.Equal(original.SpiralPath, service.Current.SpiralPath);
            var now = service.Current;
            Assert.NotSame(original, now);
            Assert.Equal(9, now.FlashFrequency);
            Assert.Equal("u-1", now.UnifiedId);
            Assert.False(now.StrictLockEnabled);
            Assert.True(now.PanicKeyEnabled);
            Assert.True(section.FindControl<Button>("BtnRestoreSettings")!.IsEnabled);
        }
        finally
        {
            if (shell != null) { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
            (original.UnifiedId, original.StrictLockEnabled, original.PanicKeyEnabled) = (oldId, oldStrict, oldPanic);
            service.RestoreFrom(original);   // writes the pre-test settings back (SaveImmediate inside)
            CoreSettings.ServiceProvider = oldProvider;
            AccountSettingsSection.NewBackupClient = oldClient;
            CoreSession.IsSessionRunningProvider = oldSession;
        }
    });
}
