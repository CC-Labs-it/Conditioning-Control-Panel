using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TrophyCaseOwnershipTests
{
    // WPF MainWindow.Enhancements.cs:1979 and MainWindow.Leaderboard.cs:1439.
    [Fact]
    public Task TrophyGateFollowsPurchaseRestoreAndVisualLifetime() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        settings.Current.UnlockedSkills = new List<string>();
        Window? window = null;
        try
        {
            var tab = new LeaderboardTabView();
            Assert.False(tab.ShowTrophyStats);
            window = new Window { Content = tab };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            // The purchase receipt replaces the list, just as profile adoption does.
            settings.Current.UnlockedSkills = new List<string> { "trophy_case" };
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.ShowTrophyStats);
            var old = settings.Current;
            settings.RestoreFrom(new AppSettings { OfflineMode = true, UnlockedSkills = new List<string>() });
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.ShowTrophyStats);
            old.UnlockedSkills = new List<string> { "trophy_case" };
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.ShowTrophyStats);
            settings.Current.UnlockedSkills = new List<string> { "trophy_case" };
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.ShowTrophyStats);
            window.Content = null;
            settings.Current.UnlockedSkills = new List<string>();
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.ShowTrophyStats); // Detached controls no longer observe settings.
            window.Content = tab;
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.ShowTrophyStats);
        }
        finally
        {
            window?.Close();
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });
}
