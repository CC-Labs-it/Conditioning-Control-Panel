using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoFeedDeferTests
{
    private sealed class Host : IMandatoryVideoHost
    {
        public readonly List<string> Shown = new();
        public void Show(string path, bool strict) => Shown.Add(path);
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    // WPF VideoFeatureControl.xaml.cs:528 and VideoService.cs:2276: Test gives a six-second notice
    // and waits until the actual feed window leaves the screen.
    [Fact]
    public Task TestVideoShowsNoticeAndWaitsForVisibleFeed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var overlay = MandatoryVideoOverlay.Instance;
        var original = overlay.Scheduler;
        var oldEngine = CoreEngine.Video;
        var setting = CoreSettings.Current.MandatoryVideosEnabled;
        var service = global::ConditioningControlPanel.Avalonia.App.Notifications;
        var hostField = service.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldHost = hostField.GetValue(service);
        var panel = new StackPanel(); var clock = new Clock(); var host = new Host();
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/v/test.mp4" })
            { FeedOwnsTheScreen = original.FeedOwnsTheScreen };
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        try
        {
            Assert.NotNull(video.FeedOwnsTheScreen);
            CoreSettings.Current.MandatoryVideosEnabled = true;
            service.AttachHost(panel); panel.Children.Clear();
            overlay.Scheduler = video; CoreEngine.Video = video; video.Start();
            feed.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(video.FeedOwnsTheScreen!());
            var button = new VideoFeatureControl().FindControl<Button>("BtnTestVideo")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(video.IsPlaying);
            var toast = Assert.Single(panel.Children);
            Assert.Contains(toast.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("video_toast_feed_has_the_screen"));
            Assert.Equal(TimeSpan.FromSeconds(6), ((DispatcherTimer)((Border)toast).Tag!).Interval);
            clock.Advance(TimeSpan.FromSeconds(2)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Shown);
            feed.Close();
            Assert.False(video.FeedOwnsTheScreen!());
            clock.Advance(TimeSpan.FromMilliseconds(500)); Dispatcher.UIThread.RunJobs();
            Assert.True(video.IsPlaying);
            clock.Advance(MandatoryVideoScheduler.PreRoll); Dispatcher.UIThread.RunJobs();
            Assert.Equal("/v/test.mp4", Assert.Single(host.Shown));
        }
        finally
        {
            video.Stop(); feed.Close(); overlay.Scheduler = original; CoreEngine.Video = oldEngine;
            CoreSettings.Current.MandatoryVideosEnabled = setting;
            foreach (var child in panel.Children.OfType<Border>()) (child.Tag as DispatcherTimer)?.Stop();
            hostField.SetValue(service, oldHost);
        }
        return Task.CompletedTask;
    });

    // WPF AutonomyService.cs:1220 and AutonomyService.VoiceCommandHost.cs:72 both use
    // TriggerVideo's non-earned default, so neither action can cover the visible feed.
    [Fact]
    public Task TakeoverAndVoiceVideoDeferPastFeed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreEngine.Video;
        var s = CoreSettings.Current;
        var saved = (s.AutonomyModeEnabled, s.AutonomyConsentGiven, s.MandatoryVideosEnabled, CoreEntitlement.HasPremiumProvider);
        var host = new Host(); var clock = new Clock(); var probes = 0;
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/v/test.mp4" })
            { FeedOwnsTheScreen = () => { probes++; return true; } };
        var shell = new MainShellWindow();
        try
        {
            (s.AutonomyModeEnabled, s.AutonomyConsentGiven, s.MandatoryVideosEnabled) = (true, true, true);
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEngine.Video = video; video.Start();
            Assert.True(shell.Autonomy.Start(withTimer: false));
            shell.PerformAutonomy(AutonomyActionType.Video);
            Assert.True(probes > 0);
            Assert.False(video.IsPlaying);
            video.ForceCleanup(); probes = 0;
            var action = (Action?)typeof(MainShellWindow).GetMethod("VoiceAction", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, new object[] { "video_on" });
            Assert.NotNull(action);
            action();
            Assert.True(probes > 0);
            Assert.False(video.IsPlaying);
        }
        finally
        {
            shell.Autonomy.Stop(); video.Stop(); CoreEngine.Video = old; shell.Close();
            (s.AutonomyModeEnabled, s.AutonomyConsentGiven, s.MandatoryVideosEnabled, CoreEntitlement.HasPremiumProvider) = saved;
        }
        return Task.CompletedTask;
    });

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<T> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new T(this, () => cb(state)); _timers.Add(t); t.Change(due, period); return t;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            for (T? next; (next = _timers.Find(t => t.Due <= end)) != null;)
            {
                foreach (var t in _timers) if (t.Due < next.Due) next = t;
                _now = next.Due!.Value; next.Due = null; next.Fire();
            }
            _now = end;
        }
        private sealed class T(Clock clock, Action fire) : ITimer
        {
            public DateTimeOffset? Due;
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) { Due = due == Timeout.InfiniteTimeSpan ? null : clock._now + due; return true; }
            public void Dispose() => Due = null;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }
}
