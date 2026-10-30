using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.LogicalTree;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BubbleCountService.TriggerGame :321 queues behind InteractionQueue: Test Now on the
/// card while a lock card is up waits (no game, no Bambi Freeze) and plays once the card closes.</summary>
public sealed class BubbleCountQueueTests
{
    [Fact]
    public Task VideoTestNowWaitsForTheBubbleRound() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var clock = new Clock();
        var b = new BubbleCountScheduler(BubbleCountHost.Instance, clock, () => Array.Empty<string>());
        var video = new MandatoryVideoScheduler(
            global::ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance,
            clock, () => new[] { "/none/clip.mp4" });
        var (oldBubble, oldVideo, oldFreeze) =
            (CoreEngine.BubbleCount, CoreEngine.Video, CoreSubliminal.BambiFreezeProvider);
        CoreEngine.BubbleCount = b;
        CoreEngine.Video = video;
        CoreSubliminal.BambiFreezeProvider = () => { };
        try
        {
            b.Trigger(forceTest: true);
            Assert.True(b.IsBusy); // the round owns even its Freeze lead-in
            var card = new VideoFeatureControl();
            card.FindControl<Button>("BtnTestVideo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(video.IsPlaying);
            b.ForceCleanup();
            clock.Advance(BubbleCountScheduler.QueuePoll);
            Dispatcher.UIThread.RunJobs();
            Assert.True(video.IsPlaying); // pre-roll begins only after the game releases the screen
        }
        finally
        {
            video.Stop(); b.ForceCleanup();
            (CoreEngine.BubbleCount, CoreEngine.Video, CoreSubliminal.BambiFreezeProvider) =
                (oldBubble, oldVideo, oldFreeze);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestNowOverALockCardWaitsForIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var clock = new Clock();
        var host = BubbleCountHost.Instance;
        var b = new BubbleCountScheduler(host, clock, () => new[] { "/none/clip.mp4" });
        var (real, prevEngine, prevFreeze) = (host.Scheduler, CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider);
        var freezes = 0;
        host.Scheduler = b;
        CoreEngine.BubbleCount = b;
        CoreSubliminal.BambiFreezeProvider = () => freezes++;
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = new BubbleCountFeatureControl();
            card.FindControl<Button>("BtnTest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(b.IsQueued);
            Assert.False(b.IsBusy);
            clock.Advance(TimeSpan.FromSeconds(10));               // the card is still up
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.IsQueued);
            Assert.Equal(0, freezes);

            LockCardWindow.ForceCloseAll();
            Dispatcher.UIThread.RunJobs();
            clock.Advance(BubbleCountScheduler.QueuePoll);
            Dispatcher.UIThread.RunJobs();
            Assert.False(b.IsQueued);
            Assert.True(b.IsBusy);                                 // the game starts its lead-in
            Assert.Equal(1, freezes);
        }
        finally
        {
            b.ForceCleanup();
            LockCardWindow.ForceCloseAll();
            (host.Scheduler, CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider) = (real, prevEngine, prevFreeze);
        }
        return Task.CompletedTask;
    });

    /// <summary>Audit #1932: with no LibVLC a strict game is skipped as WPF does (BubbleCountService.cs:373),
    /// not counted as a failure - no window, no WRONG! WATCH AGAIN retry, the scheduler is idle again.</summary>
    [Fact]
    public Task StrictGameWithoutLibVlcIsSkippedNotRetried() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var clip = System.IO.Path.GetTempFileName();
        var clock = new Clock();
        var host = BubbleCountHost.Instance;
        var b = new BubbleCountScheduler(host, clock, () => new[] { clip });
        var s = CoreSettings.Current;
        var (real, prevEngine, strict) = (host.Scheduler, CoreEngine.BubbleCount, s.BubbleCountStrictLock);
        var shared = typeof(global::ConditioningControlPanel.Avalonia.Platform.LibVlcAudio).GetProperty("Shared",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var vlc = shared.GetValue(null);
        var (prevStarted, started) = (CoreProgression.TrackBubbleCountGameStartedProvider, 0);
        CoreProgression.TrackBubbleCountGameStartedProvider = () => started++;
        host.Scheduler = b;
        CoreEngine.BubbleCount = b;
        s.BubbleCountStrictLock = true;
        shared.SetValue(null, null);
        try
        {
            b.Trigger(forceTest: true);
            clock.Advance(BubbleCountScheduler.LeadIn);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(BubbleCountWindow.OpenWindows);
            Assert.Empty(host.Messages);                           // no WRONG! WATCH AGAIN
            Assert.False(b.IsBusy);                                // skipped: the scheduler is free again
            Assert.Equal(0, started);                              // WPF skips before counting it started
        }
        finally
        {
            shared.SetValue(null, vlc);
            CoreProgression.TrackBubbleCountGameStartedProvider = prevStarted;
            b.ForceCleanup();
            host.CloseAll();
            (host.Scheduler, CoreEngine.BubbleCount, s.BubbleCountStrictLock) = (real, prevEngine, strict);
            System.IO.File.Delete(clip);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PauseEffectsEditorPersistsAndFollowsSettingsReplacement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        settings.Current.BubbleCountPauseEffects = false;
        CoreSettings.ServiceProvider = () => settings;
        var card = new BubbleCountFeatureControl();
        var window = new Window { Content = card };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var check = card.FindControl<CheckBox>("ChkPauseEffects")!;
            Assert.NotNull(check);
            Assert.False(check.IsChecked);
            check.IsChecked = true;
            settings.SaveImmediate();
            var restored = new SettingsService();
            Assert.True(restored.Current.BubbleCountPauseEffects);
            restored.Current.BubbleCountPauseEffects = false;
            CoreSettings.ServiceProvider = () => restored;
            card.RebindToCurrentSettings();
            Assert.False(check.IsChecked);
            restored.Current.BubbleCountPauseEffects = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(check.IsChecked); // external setting changes repaint the loaded rack panel
            check.IsChecked = false;
            Assert.False(restored.Current.BubbleCountPauseEffects);
            Assert.True(settings.Current.BubbleCountPauseEffects); // discarded settings stay untouched
        }
        finally { window.Close(); CoreSettings.ServiceProvider = oldProvider; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestNowOverForYouWaitsWithoutFreezingThenReplaysOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var clock = new Clock();
        var host = BubbleCountHost.Instance;
        var b = new BubbleCountScheduler(host, clock, () => new[] { "/none/clip.mp4" });
        var (oldEngine, oldFreeze) = (CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider);
        var freezes = 0;
        CoreEngine.BubbleCount = b;
        CoreSubliminal.BambiFreezeProvider = () => freezes++;
        // No browser navigation or remote media: just the real feed window's visible lifecycle.
        var feed = new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow(
            global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow.Games["fyp"]);
        try
        {
            feed.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.FeedOwnsTheScreen);
            var button = new BubbleCountFeatureControl().FindControl<Button>("BtnTest")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            clock.Advance(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.False(b.IsBusy);
            Assert.Equal(0, freezes);
            feed.Close();
            Assert.False(host.FeedOwnsTheScreen);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.IsBusy);
            Assert.Equal(1, freezes);
            MainShellWindow.StopEngine(); // the existing panic/engine route cancels the deferred/game state
            clock.Advance(TimeSpan.FromSeconds(2));
            Dispatcher.UIThread.RunJobs();
            Assert.False(b.IsBusy);
            Assert.Empty(BubbleCountWindow.OpenWindows);
        }
        finally
        {
            b.ForceCleanup(); feed.Close();
            (CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider) = (oldEngine, oldFreeze);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmptyLibraryTestNowShowsTheLocalizedWarningToast() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var hostField = typeof(global::ConditioningControlPanel.Avalonia.Helpers.NotificationService)
            .GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var service = global::ConditioningControlPanel.Avalonia.App.Notifications;
        var oldHost = hostField.GetValue(service);
        var oldPacks = ContentPackStore.Current;
        var clock = new Clock();
        var b = new BubbleCountScheduler(BubbleCountHost.Instance, clock, () => Array.Empty<string>());
        var panel = new StackPanel();
        try
        {
            ContentPackStore.Current = null;
            service.AttachHost(panel);
            panel.Children.Clear();
            b.Trigger(forceTest: true);
            clock.Advance(BubbleCountScheduler.LeadIn);
            Dispatcher.UIThread.RunJobs();
            Assert.False(b.IsBusy);
            var toast = Assert.Single(panel.Children);
            Assert.Contains(toast.GetLogicalDescendants().OfType<TextBlock>(), t =>
                t.Text == global::ConditioningControlPanel.Localization.Loc.Get("bubblecount_no_videos"));
            Assert.Empty(BubbleCountWindow.OpenWindows);
        }
        finally
        {
            b.ForceCleanup(); ContentPackStore.Current = oldPacks;
            foreach (var child in panel.Children.OfType<Border>()) (child.Tag as DispatcherTimer)?.Stop();
            hostField.SetValue(service, oldHost);
        }
        return Task.CompletedTask;
    });

    /// <summary>Stepped clock: timers fire only on <see cref="Advance"/>.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<T> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new T(this, () => cb(state));
            _timers.Add(t);
            t.Change(due, period);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            for (T? next; (next = _timers.Find(t => t.Due <= end)) != null;)
            {
                foreach (var t in _timers) if (t.Due < next.Due) next = t;
                _now = next.Due!.Value;
                next.Due = null;
                next.Fire();
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
