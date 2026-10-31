using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class TabFirstShowWorkTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public Task FoldedGalleryCreatesNoItems_ExpansionUsesLatestProfile_RepeatExpansionReusesItems() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var view = new DiscordTabView();
            var grid = view.FindControl<ItemsControl>("ProfileAchievementGrid")!;
            var expander = view.FindControl<Expander>("ProfileAllAchievementsExpander")!;
            var ids = Achievement.All.Keys.Take(2).ToArray();
            Assert.Equal(2, ids.Length);
            view.ShowAchievements(ids, "empty");
            Assert.Null(grid.ItemsSource);
            // A lookup completes while folded: opening must use the new user's data.
            view.ShowAchievements(ids.Take(1), "empty");
            expander.IsExpanded = true;
            Assert.Single(grid.Items);
            var items = grid.ItemsSource;
            expander.IsExpanded = false;
            expander.IsExpanded = true;
            Assert.Same(items, grid.ItemsSource);
            view.ShowAchievements(null, "no unlocks");
            Assert.Null(grid.ItemsSource);
            Assert.Equal("no unlocks", view.FindControl<TextBlock>("TxtNoAchievements")!.Text);
            return Task.CompletedTask;
        });

    [Fact]
    public Task RemoteArtIsBoundedAndRetainedAcrossReattachment() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var loads = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var view = new RemoteControlTabView(() =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Interlocked.Increment(ref loads);
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "Worker decode was not released");
            return ModArt.TryLoad("features/remote_control.png", 800);
        });
        var window = new Window { Content = view };
        try
        {
            window.Show();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // Draining UI work cannot make an unfinished worker decode ready.
            Dispatcher.UIThread.RunJobs();
            var plate = view.FindControl<Border>("HeroArtPlate")!;
            Assert.Null(plate.Background);
            Assert.False(view.FeatureArtReady.IsCompleted);
            release.Set();
            await view.FeatureArtReady.WaitAsync(TimeSpan.FromSeconds(30));
            var brush = Assert.IsType<ImageBrush>(plate.Background);
            Assert.NotNull(brush.Source);
            Assert.InRange(Assert.IsType<global::Avalonia.Media.Imaging.Bitmap>(brush.Source).PixelSize.Width, 1, 800);
            var load = view.FeatureArtReady;
            window.Content = null;
            window.Content = view;
            Assert.Same(load, view.FeatureArtReady);
            Assert.Same(brush, plate.Background);
            Assert.Equal(1, loads);
        }
        finally { release.Set(); window.Close(); }
    });
}
