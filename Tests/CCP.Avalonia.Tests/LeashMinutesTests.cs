using System;
using System.IO;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class LeashMinutesTests
{
    [Fact]
    public void DayLogMinutesReachWireAndCompleteOnlyTodaysAssignment()
    {
        var property = typeof(AvApp).GetProperty("FeatureDayLog", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldLog = property.GetValue(null);
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var dir = Directory.CreateTempSubdirectory("ccp-leash-minutes-");
        FeatureDayLogService? log = null;
        try
        {
            CoreSettings.Current.TotalConditioningMinutes = 100;
            log = new FeatureDayLogService(() => new AchievementProgress(), Path.Combine(dir.FullName, "feature_day_log.json"));
            property.SetValue(null, log);
            CoreSettings.Current.TotalConditioningMinutes = 117;
            log.Tick();
            var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", dayInputs: LeashHead.DayInputs);
            var block = LeashHeadTests.BlockWithPunishment();
            var assignment = new JObject { ["aid"] = "abcdef0123456789", ["kind"] = "minutes", ["size"] = 15,
                ["day"] = LeashAssignRule.DayKey(DateTime.Now), ["status"] = "open", ["at"] = DateTimeOffset.UtcNow.ToString("o") };
            block["me"]!["assignment"] = assignment;
            svc.ApplyBlock(block);
            Assert.NotNull(svc.Snapshot.Me?.Assignment);
            var report = svc.BuildReportJson()!;
            Assert.Equal(17, (int)report["minutes"]!);
            Assert.True((bool)report["assign_done"]!);
            assignment["day"] = LeashAssignRule.DayKey(DateTime.Now.AddDays(-1));
            svc.ApplyBlock(block);
            Assert.False((bool)svc.BuildReportJson()!["assign_done"]!);
            property.SetValue(null, null);
            Assert.Equal(0, LeashHead.DayInputs()!.Value.MinutesToday);
        }
        finally
        {
            log?.Dispose();
            property.SetValue(null, oldLog);
            CoreSettings.ServiceProvider = oldSettings;
            dir.Delete(true);
        }
    }
}
