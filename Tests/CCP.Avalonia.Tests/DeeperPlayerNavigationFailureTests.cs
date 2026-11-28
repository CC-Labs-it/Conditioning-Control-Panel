using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using FluentIcons.Common;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayerNavigationFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedNavigationReportsErrorAndDiscardsPendingPlayAfterPanic(bool panic) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var player = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Video }, "test");
        var web = player.FindControl<WebHost>("VideoBrowser")!;
        var play = new TaskCompletionSource<string?>();
        web.ScriptInvoker = script => script.Contains("l.play();") ? play.Task : Task.FromResult<string?>("");
        var uri = new Uri("https://hypnotube.com/video/test");
        try
        {
            player.Show();
            typeof(EnhancementPlayerWindow).GetMethod("LoadVideoUrl", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object[] { uri.ToString() });
            player.FindControl<Button>("BtnPlayPause")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            web.OnNavigationCompleted(uri, success: false);
            var expected = Loc.GetF("deeper_player_video_nav_failed_fmt", uri.Host);
            Assert.Equal(expected, player.FindControl<TextBlock>("TxtStatus")!.Text);
            var status = player.FindControl<TextBlock>("TxtVideoStatus")!;
            Assert.True(status.IsVisible);
            Assert.Equal(expected, status.Text);
            Assert.Equal(Symbol.Play, player.FindControl<IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            var entries = (ObservableCollection<EventLogEntry>)typeof(EnhancementPlayerWindow).GetField("_logEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
            Assert.Contains(entries, e => e.Category == EventLogEntry.EventLogCategory.Error && e.Description == expected);
            if (panic) PanicSurfaces.All.Single(s => s.Id == "deeper-player").Stop(null);
            play.SetResult("ok");
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Symbol.Play, player.FindControl<IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            Assert.Equal(panic ? Loc.Get("deeper_player_status_stopped") : expected, player.FindControl<TextBlock>("TxtStatus")!.Text);
        }
        finally { player.Close(); }
    });

    // WPF EnhancementPlayerWindow.xaml.cs:1825-1842 keeps the source bound after a failure.
    [Fact]
    public Task FailedNavigationLeavesPlayWorkingAfterTheNextSuccess() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var player = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Video }, "test");
        var web = player.FindControl<WebHost>("VideoBrowser")!;
        web.ScriptInvoker = script => Task.FromResult<string?>(script.Contains("l.play();") ? "ok" : "");
        var uri = new Uri("https://hypnotube.com/video/test");
        try
        {
            player.Show();
            typeof(EnhancementPlayerWindow).GetMethod("LoadVideoUrl", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object[] { uri.ToString() });
            web.OnNavigationCompleted(uri, success: false);
            web.OnNavigationCompleted(uri, success: true);
            player.FindControl<Button>("BtnPlayPause")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Symbol.Pause, player.FindControl<IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            Assert.Equal(Loc.Get("deeper_player_status_playing"), player.FindControl<TextBlock>("TxtStatus")!.Text);
        }
        finally { player.Close(); }
    });
}
