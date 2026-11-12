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
