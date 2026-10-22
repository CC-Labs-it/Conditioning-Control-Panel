using System;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Fact]
    public async Task SignedInStartupFetchesPillsWithoutOpeningLeaderboard()
    {
        var s = CoreSettings.Current;
        var oldClient = LeaderboardTabView.NewClient;
        var oldOffline = s.OfflineMode;
        var oldSkills = s.UnlockedSkills.ToList();
        var wire = new BoardWire();
        var oldDelay = MainShellWindow.StartupPillFetchDelay;
        try
        {
            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            MainShellWindow.StartupPillFetchDelay = TimeSpan.Zero;
            CoreAccount.IsLoggedInProvider = () => true;
            s.OfflineMode = false;
            s.UnlockedSkills.AddRange(new[] { "hive_mind", "popular_girl" });
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                        .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions
                            { UseHeadlessDrawing = false }).SetupWithoutStarting();
                global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
                var shell = new MainShellWindow();
                try
                {
                    shell.Show();
                    await shell.StartupPillFetch;
                    Assert.Single(wire.Seen);
                    Assert.Contains("season=all-time", wire.Seen[0]);
                    Assert.Equal("2", shell.Named<TextBlock>("TxtPillOnlineUsers")!.Text);
                    Assert.Equal("Top 1%", shell.Named<TextBlock>("TxtPillRankPercentile")!.Text);
                    CoreAccount.UnifiedUserId = "local-player";
                    MainShellWindow.NoteLeaderboardPage(new LeaderboardPage<LeaderboardEntryData>
                    {
                        TotalUsers = 100, OnlineUsers = 3,
                        Entries = new System.Collections.Generic.List<LeaderboardEntryData>
                        {
                            new() { UnifiedId = "other" }, new() { UnifiedId = "local-player" }
                        }
                    });
                    shell.UpdateStatPills();
                    Assert.Equal("Top 2%", shell.Named<TextBlock>("TxtPillRankPercentile")!.Text);
                    s.OfflineMode = true;
                    await shell.RefreshStatPillLeaderboardAsync();
                    Assert.Single(wire.Seen);
                    s.OfflineMode = false;
                    CoreAccount.IsLoggedInProvider = () => false;
                    await shell.RefreshStatPillLeaderboardAsync();
                    Assert.Single(wire.Seen);
                }
                finally { shell.Close(); }
            });
        }
        finally
        {
            LeaderboardTabView.NewClient = oldClient; s.OfflineMode = oldOffline;
            s.UnlockedSkills.Clear(); s.UnlockedSkills.AddRange(oldSkills);
            MainShellWindow.StartupPillFetchDelay = oldDelay;
            MainShellWindow.NoteLeaderboardPage(new LeaderboardPage<LeaderboardEntryData>());
        }
    }
}
