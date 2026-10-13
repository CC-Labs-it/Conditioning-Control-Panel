using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// FeatureDayLogService moved from the WPF head to Core: both heads now read and write the user's
/// feature_day_log.json, so a file the WPF build wrote must load whole, read the same, and not be
/// rewritten by a run that changed nothing; and what Core writes must be WPF's exact shape.
/// </summary>
public sealed class FeatureDayLogServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-daylog-").FullName;
    private string LogPath => Path.Combine(_dir, "feature_day_log.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string FixturePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "feature_day_log_wpf.json");

    private static string Lf(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void WpfWrittenLog_LoadsWhole_ReadsMinutes_AndIsNotRewritten()
    {
        File.Copy(FixturePath(), LogPath);
        var before = File.ReadAllBytes(LogPath);

        var svc = new FeatureDayLogService(() => null, LogPath);
        try
        {
            // Every field survives the load: re-serialized the way the service writes, it is the file.
            var again = JsonSerializer.Serialize(svc.Log, new JsonSerializerOptions { WriteIndented = true });
            Assert.Equal(Lf(File.ReadAllText(FixturePath())).TrimEnd(), Lf(again));

            // The leash day report's minutes (WPF LeashService.AppDayInputs: the day's cm).
            Assert.Equal(47, svc.ConditioningMinutesOn(new DateTime(2026, 10, 1, 23, 59, 0)));
            Assert.Equal(0, svc.ConditioningMinutesOn(new DateTime(2026, 9, 30, 8, 0, 0)));
            Assert.Equal(0, svc.ConditioningMinutesOn(new DateTime(2026, 10, 2)));
            Assert.Equal(47, svc.ConditioningMinutesOn("2026-10-01")); // WPF Chaster MinutesFromDayLog's key form
        }
        finally { svc.Dispose(); }

        Assert.Equal(before, File.ReadAllBytes(LogPath));
    }

    [Fact]
    public void Note_WritesWpfShape_AndReloads()
    {
        string today;
        using (var svc = new FeatureDayLogService(() => null, LogPath))
        {
            svc.Note("e_bt", 2);
            svc.Note("not_a_key");
            svc.SaveNow();
            today = svc.Log.Days[0].D; // the service's own day key: no midnight race (P08)
        }
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", today);

        var expected = "{\n  \"baseline\": {},\n  \"days\": [\n    {\n      \"d\": \"" + today + "\",\n" +
            string.Concat(Array.ConvertAll(FeatureDayEntry.CounterKeys, k => $"      \"{k}\": 0,\n")) +
            "      \"ev\": {\n        \"e_bt\": 2\n      }\n    }\n  ]\n}";
        Assert.Equal(expected, Lf(File.ReadAllText(LogPath)));

        using var reloaded = new FeatureDayLogService(() => null, LogPath);
        Assert.Equal(2, reloaded.Log.Days[0].Get("e_bt"));
    }

    [Fact]
    public void CorruptOrMissingFile_StartsEmpty_WithoutWritingOrThrowing()
    {
        // WPF behaviour, kept: an unreadable file means a fresh log in memory; nothing is written
        // until something is credited, so a run that credits nothing leaves the bad file as it was.
        File.WriteAllText(LogPath, "{ not json");
        using (var svc = new FeatureDayLogService(() => null, LogPath))
        {
            Assert.Empty(svc.Log.Days);
            Assert.Empty(svc.Log.Baseline);
        }
        Assert.Equal("{ not json", File.ReadAllText(LogPath));

        File.Delete(LogPath);
        using (var svc = new FeatureDayLogService(() => null, LogPath))
            Assert.Equal(0, svc.ConditioningMinutesOn(DateTime.Now));
        Assert.False(File.Exists(LogPath));
    }

    [Fact]
    public void CountersCreditToday_FromTheProgressSeam()
    {
        var progress = new AchievementProgress { TotalFlashImages = 10 };
        var restore = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => settings;
        try
        {
            using var svc = new FeatureDayLogService(() => progress, LogPath);
            Assert.Equal(10, svc.Log.Baseline["fl"]);
            progress.TotalFlashImages = 13;
            svc.Tick();
            Assert.Equal(3, svc.Log.Days[0].Fl);
        }
        finally { ConditioningControlPanel.CoreSettings.ServiceProvider = restore; }
    }
}
