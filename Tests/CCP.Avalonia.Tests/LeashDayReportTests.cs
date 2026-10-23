using System;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeashDayReportTests
{
    [Fact]
    public void TodaysConditioningMinutesReachThePollAndCompleteAMinutesAssignment()
    {
        var property = typeof(global::ConditioningControlPanel.Avalonia.App).GetProperty("FeatureDayLog", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        using var log = new FeatureDayLogService(() => null, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        log.Log.GetOrAddDay(DateTime.Today.ToString("yyyy-MM-dd")).Add("cm", 30);
        property.SetValue(null, log);
        try
        {
            var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", dayInputs: LeashHead.DayInputs);
            var block = LeashHeadTests.BlockWithPunishment();
            block["me"]!["assignment"] = new Newtonsoft.Json.Linq.JObject
            {
                ["aid"] = "a1", ["kind"] = "minutes", ["size"] = 30, ["day"] = DateTime.Today.ToString("yyyyMMdd"),
                ["status"] = "open", ["at"] = DateTimeOffset.UtcNow.ToString("o"),
            };
            svc.ApplyBlock(block);
            var report = svc.BuildReportJson()!;
            Assert.Equal(30, (int)report["minutes"]!);
            Assert.True((bool)report["assign_done"]!);
            log.Log.GetOrAddDay(DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd")).Add("cm", 90);
            Assert.Equal(30, LeashHead.DayInputs()!.Value.MinutesToday);
            property.SetValue(null, null);
            Assert.Equal(0, LeashHead.DayInputs()!.Value.MinutesToday);
        }
        finally { property.SetValue(null, previous); }
    }
}
