using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;
public class InstallDateStartupTests
{
    [Fact]
    public async Task InstallDateSaveOffersRecoveredSessionAndRampValuesToBackup()
    {
        var settingsFile = Path.Combine(CorePaths.UserData, "settings.json");
        var previousBackup = CoreSettingsHooks.CloudBackup;
        var service = new SettingsService();
        var offered = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            CoreSettingsHooks.CloudBackup = null;
            service.Current.InstallDate = null;
            service.Current.FlashFrequency = 77;
            service.Current.FlashOpacity = 88;
            service.SaveImmediate(suppressCloudBackup: true);
            var saved = new AppSettings { FlashFrequency = 20, FlashOpacity = 35 };
            SessionSettingsJournal.Write(CorePaths.UserData, SessionSettingsJournal.Build(saved, null, null, null, null, 1));
            SessionSettingsJournal.WriteRamp(CorePaths.UserData,
                SessionSettingsJournal.BuildRamp(new Dictionary<string, double> { [nameof(AppSettings.FlashOpacity)] = 55 }, 2));
            CoreSettingsHooks.CloudBackup = () =>
            {
                try { offered.TrySetResult(JObject.Parse(File.ReadAllText(settingsFile))); }
                catch (Exception ex) { offered.TrySetException(ex); }
                return Task.CompletedTask;
            };

            Assert.True(SessionSettingsJournal.HealOnLaunch(CorePaths.UserData, service.Current));
            service.Save();
            AvApp.RecordInstallDate(service);
            var backup = await offered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(20, (int)backup[nameof(AppSettings.FlashFrequency)]!);
            Assert.Equal(35, (int)backup[nameof(AppSettings.FlashOpacity)]!);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", (string)backup[nameof(AppSettings.InstallDate)]!);
        }
        finally
        {
            CoreSettingsHooks.CloudBackup = previousBackup;
            service.SealForReset();
            SessionSettingsJournal.Clear(CorePaths.UserData);
            SessionSettingsJournal.ClearRamp(CorePaths.UserData);
        }
    }

    [Fact]
    public async Task InstallDateSaveAfterInterruptedLockdownOffersRestoredSafetySettings()
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var recovery = Path.Combine(CorePaths.UserData, "lockdown_recovery.json");
        var previousBackup = CoreSettingsHooks.CloudBackup;
        var previousProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var offered = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            CoreSettingsHooks.CloudBackup = null;
            CoreSettings.ServiceProvider = () => service;
            service.Current.InstallDate = null;
            service.Current.PanicKeyEnabled = false;
            service.Current.StrictLockEnabled = true;
            service.SaveImmediate(suppressCloudBackup: true);
            File.WriteAllText(recovery, "{\"PanicKeyEnabled\":true,\"StrictLockEnabled\":false}");
            CoreSettingsHooks.CloudBackup = () =>
            {
                try { offered.TrySetResult(JObject.Parse(File.ReadAllText(path))); }
                catch (Exception ex) { offered.TrySetException(ex); }
                return Task.CompletedTask;
            };

            LockdownService.RecoverIfNeeded();
            AvApp.RecordInstallDate(service);
            var backup = await offered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True((bool)backup[nameof(AppSettings.PanicKeyEnabled)]!);
            Assert.False((bool)backup[nameof(AppSettings.StrictLockEnabled)]!);
            Assert.False(File.Exists(recovery));
            Assert.NotNull(service.Current.InstallDate);
        }
        finally
        {
            CoreSettingsHooks.CloudBackup = previousBackup;
            CoreSettings.ServiceProvider = previousProvider;
            service.SealForReset();
            if (File.Exists(recovery)) File.Delete(recovery);
        }
    }

    [Fact]
    public void CapturePrecedesRecoveryAndPreservesOldSettingsEvidence()
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var recovery = Path.Combine(CorePaths.UserData, "lockdown_recovery.json");
        var previousProvider = CoreSettings.ServiceProvider;
        var previousBackup = CoreSettingsHooks.CloudBackup;
        var service = new SettingsService();
        try
        {
            CoreSettingsHooks.CloudBackup = null;
            CoreSettings.ServiceProvider = () => service;
            service.Current.InstallDate = null;
            service.Current.PanicKeyEnabled = false;
            service.SaveImmediate(suppressCloudBackup: true);
            File.SetCreationTimeUtc(path, new DateTime(2021, 2, 3, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(new DateTime(2021, 2, 3), File.GetCreationTimeUtc(path).Date);
            File.WriteAllText(recovery, "{\"PanicKeyEnabled\":true,\"StrictLockEnabled\":false}");
            var captured = AvApp.CaptureInstallDate(service);
            LockdownService.RecoverIfNeeded();
            AvApp.RecordInstallDate(service, captured);
            Assert.Equal("2021-02-03", service.Current.InstallDate);
            Assert.True(service.Current.PanicKeyEnabled);
        }
        finally
        {
            CoreSettings.ServiceProvider = previousProvider;
            CoreSettingsHooks.CloudBackup = previousBackup;
            service.SealForReset();
            if (File.Exists(recovery)) File.Delete(recovery);
        }
    }

    [Fact]
    public void StartupRecordsDateOnlyOnceInSandboxSettings()
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        if (File.Exists(path)) File.Delete(path);
        var service = new SettingsService();
        AvApp.RecordInstallDate(service);
        var first = (string?)JObject.Parse(File.ReadAllText(path))["InstallDate"];
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", first!);
        service.Current.InstallDate = "2021-02-03";
        service.SaveImmediate();
        AvApp.RecordInstallDate(new SettingsService());
        Assert.Equal("2021-02-03", (string?)JObject.Parse(File.ReadAllText(path))["InstallDate"]);
    }
}
