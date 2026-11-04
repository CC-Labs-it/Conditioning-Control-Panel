using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SeasonBookkeepingWireTests
{
    [Fact]
    public Task ShellRollsOnAnAdoptedSeasonKey() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.CurrentSeason = "1903-01";
        s.SeasonStatsSeason = "1903-01";
        s.LastSeasonResetSeen = "1903-01";
        s.SeasonConditioningMinutes = 8;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            s.CurrentSeason = "1903-02";
            Assert.Equal("1903-02", s.SeasonStatsSeason);
            Assert.Equal(8, SeasonRecapStore.Load("1903-01")!.SeasonMinutes);
            ConditioningTimeTracker.AddConditioningTime(s, 2);
            Assert.Equal(2, s.SeasonConditioningMinutes);
        }
        finally { shell.Close(); SettingsService.DisarmAllPendingSaves(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task RestoredSettingsAreReboundAndUseLeaderboardPercentile() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            MainShellWindow.NoteLeaderboardPage(new LeaderboardPage<LeaderboardEntryData> { YourRank = 2, YourTotal = 10 });
            var restored = new AppSettings { CurrentSeason = "1905-02", SeasonStatsSeason = "1905-01",
                LastSeasonResetSeen = "1905-01", SeasonConditioningMinutes = 8 };
            settings.RestoreFrom(restored); // replacement is reconciled immediately
            Assert.Equal("1905-02", restored.SeasonStatsSeason);
            Assert.Equal(20, SeasonRecapStore.Load("1905-01")!.Percentile);
            ConditioningTimeTracker.AddConditioningTime(restored, 2);
            restored.CurrentSeason = "1905-03";
            Assert.Equal("1905-03", restored.SeasonStatsSeason);
            Assert.Equal(2, SeasonRecapStore.Load("1905-02")!.SeasonMinutes);
            shell.Close();
            restored.CurrentSeason = "1905-04";
            Assert.Equal("1905-03", restored.SeasonStatsSeason);
            var afterClose = new AppSettings { CurrentSeason = "1906-02", SeasonStatsSeason = "1906-01",
                LastSeasonResetSeen = "1906-01", SeasonConditioningMinutes = 4 };
            settings.RestoreFrom(afterClose);
            Assert.Equal("1906-01", afterClose.SeasonStatsSeason);
        }
        finally { shell.Close(); SettingsService.DisarmAllPendingSaves(); }
        return Task.CompletedTask;
    });
}
