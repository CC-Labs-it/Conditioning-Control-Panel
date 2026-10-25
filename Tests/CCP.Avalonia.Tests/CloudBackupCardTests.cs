using System;
using System.Collections.Generic;
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
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
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
        try
        {
            if (AccountSeed.Patreon == null) Assert.Null(AccountSettingsSection.NewBackupClient());

            CoreSettings.ServiceProvider = () => service;
            CoreSession.IsSessionRunningProvider = () => false;
            AccountSettingsSection.NewBackupClient =
                () => new CloudSettingsBackup(() => CoreSettings.Current, wire, () => "tok");

            original.UnifiedId = null;
            var section = new AccountSettingsSection();
            var card = section.FindControl<Border>("CloudSettingsBackupSection")!;
            Assert.False(card.IsVisible);                                     // WPF signed-out state: shut

            original.UnifiedId = "u-1";
            original.StrictLockEnabled = false;
            original.PanicKeyEnabled = true;
            section.Refresh();
            Assert.True(card.IsVisible);

            var status = section.FindControl<TextBlock>("TxtCloudBackupStatus")!;
            await section.UpdateBackupStatusAsync();
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t"), status.Text);
            wire.Backup = "{\"app_version\":\"7.1.5\",\"backed_up_at\":\"2026-10-09T18:30:00Z\",\"size_bytes\":10}";
            await section.UpdateBackupStatusAsync();
            Assert.Contains("7.1.5", status.Text);

            wire.Paths.Clear();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.Delay = gate.Task;
            var pending = section.BackupNowAsync();
            var backupBtn = section.FindControl<Button>("BtnBackupSettingsNow")!;
            Assert.False(backupBtn.IsEnabled);
            Assert.Equal("btn_backing_up", Assert.IsType<ConditioningControlPanel.Avalonia.Controls.IconLabel>(backupBtn.Content).Key);
            gate.SetResult();
            await pending;
            wire.Delay = null;
            Assert.Equal("/v2/user/backup-settings", wire.Paths[0]);
            Assert.True(backupBtn.IsEnabled);
            Assert.Equal("btn_backup_now", Assert.IsType<ConditioningControlPanel.Avalonia.Controls.IconLabel>(backupBtn.Content).Key);

            var evil = JObject.FromObject(new AppSettings());
            evil["FlashFrequency"] = 9;
            evil["StrictLockEnabled"] = true;
            evil["PanicKeyEnabled"] = false;
            evil["UnifiedId"] = "someone-else";
            wire.Backup = "{\"app_version\":\"7.1.5\",\"settings_data\":\"" + Pack(evil) + "\"}";
            wire.Paths.Clear();
            CoreSession.IsSessionRunningProvider = () => true;
            await section.RestoreFromCloudAsync();
            Assert.Empty(wire.Paths);
            Assert.Same(original, service.Current);

            CoreSession.IsSessionRunningProvider = () => false;
            await section.RestoreFromCloudAsync();
            Assert.Equal("/v2/user/settings-backup", wire.Paths[0]);
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
            (original.UnifiedId, original.StrictLockEnabled, original.PanicKeyEnabled) = (oldId, oldStrict, oldPanic);
            service.RestoreFrom(original);   // writes the pre-test settings back (SaveImmediate inside)
            CoreSettings.ServiceProvider = oldProvider;
            AccountSettingsSection.NewBackupClient = oldClient;
            CoreSession.IsSessionRunningProvider = oldSession;
        }
    });
}
