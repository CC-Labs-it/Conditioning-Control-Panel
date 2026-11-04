using System.Threading.Tasks;
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
}
