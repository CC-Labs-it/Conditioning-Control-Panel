using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountNativeTeardownTests
{
    [Fact]
    public void Settled_stop_runs_off_caller_before_freeing_the_sink()
    {
        var caller = Environment.CurrentManagedThreadId; var stopped = false; var disposed = false; var freed = false; Assert.True(BubbleCountWindow.StopNative(() =>
        {
            Assert.NotEqual(caller, Environment.CurrentManagedThreadId);
            stopped = true;
        }, () => { Assert.True(stopped); disposed = true; },
            () => { Assert.True(disposed); freed = true; }, new object(), 2000));
        Assert.True(freed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stuck_or_failed_stop_roots_resources_and_blocks_native_reuse(bool throws)
    {
        var previous = BubbleCountWindow.NativeUnavailable; var previousAvailable = BubbleCountHost.NativePlayerAvailable; BubbleCountHost.NativePlayerAvailable = () => true;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim(); using var cleaned = new ManualResetEventSlim(); var disposed = false;
        var freed = false;
        try
        {
            BubbleCountWindow.NativeUnavailable = false; var watch = Stopwatch.StartNew(); Assert.False(BubbleCountWindow.StopNative(() =>
            {
                entered.Set();
                try { if (throws) throw new InvalidOperationException(); else release.Wait(); }
                finally { finished.Set(); }
            }, () => disposed = true, () => freed = true, resources, 500,
                action => { action(); cleaned.Set(); }));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3)); Assert.True(entered.Wait(TimeSpan.FromSeconds(2))); Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.Equal(!throws, BubbleCountHost.Instance.CanPlay); Assert.Contains(resources, quarantined); Assert.False(freed);
            Assert.False(disposed); release.Set(); Assert.True(finished.Wait(TimeSpan.FromSeconds(2)));
            if (throws)
            {
                Assert.Contains(resources, quarantined); Assert.True(BubbleCountWindow.NativeUnavailable); Assert.False(freed);
                Assert.False(disposed);
            }
            else
            {
                Assert.True(cleaned.Wait(TimeSpan.FromSeconds(2))); Assert.True(disposed); Assert.True(freed); Assert.DoesNotContain(resources, quarantined);
                Assert.False(BubbleCountWindow.NativeUnavailable); Assert.True(BubbleCountHost.Instance.CanPlayPath("next.mp4"));
            }
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(2));
            if (!throws) cleaned.Wait(TimeSpan.FromSeconds(2));
            lock (quarantined) quarantined.Remove(resources); BubbleCountWindow.NativeUnavailable = previous; BubbleCountHost.NativePlayerAvailable = previousAvailable;
        }
    }
    [Fact]
    public void Stuck_disposal_also_quarantines_without_freeing_the_sink()
    {
        var previous = BubbleCountWindow.NativeUnavailable; var previousAvailable = BubbleCountHost.NativePlayerAvailable; BubbleCountHost.NativePlayerAvailable = () => true;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object(); using var release = new ManualResetEventSlim(); using var finished = new ManualResetEventSlim();
        using var cleaned = new ManualResetEventSlim(); var freed = false;
        try
        {
            BubbleCountWindow.NativeUnavailable = false; Assert.False(BubbleCountWindow.StopNative(() => { }, () =>
            {
                try { release.Wait(); } finally { finished.Set(); }
            }, () => freed = true, resources, 500, action => { action(); cleaned.Set(); })); Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.Contains(resources, quarantined); Assert.False(freed); release.Set(); Assert.True(finished.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(cleaned.Wait(TimeSpan.FromSeconds(2))); Assert.True(freed); Assert.DoesNotContain(resources, quarantined);
            Assert.False(BubbleCountWindow.NativeUnavailable); Assert.True(BubbleCountHost.Instance.CanPlayPath("next.mp4"));
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(2));
            cleaned.Wait(TimeSpan.FromSeconds(2));
            lock (quarantined) quarantined.Remove(resources); BubbleCountWindow.NativeUnavailable = previous; BubbleCountHost.NativePlayerAvailable = previousAvailable;
        }
    }

    [Fact]
    public void Failed_disposal_remains_quarantined()
    {
        var previous = BubbleCountWindow.NativeUnavailable; var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object(); var freed = false;
        try
        {
            Assert.False(BubbleCountWindow.StopNative(() => { }, () => throw new InvalidOperationException(),
                () => freed = true, resources, 2000, action => action()));
            Assert.True(BubbleCountWindow.NativeUnavailable); Assert.Contains(resources, quarantined); Assert.False(freed);
        }
        finally
        {
            lock (quarantined) quarantined.Remove(resources); BubbleCountWindow.NativeUnavailable = previous;
        }
    }

    [Fact]
    public void Failed_sink_cleanup_is_not_retried_and_stays_quarantined()
    {
        var previous = BubbleCountWindow.NativeUnavailable; var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object(); var calls = 0;
        try
        {
            Assert.False(BubbleCountWindow.StopNative(() => { }, () => { },
                () => { calls++; throw new InvalidOperationException(); }, resources, 2000, action => action()));
            Assert.Equal(1, calls); Assert.True(BubbleCountWindow.NativeUnavailable); Assert.Contains(resources, quarantined);
        }
        finally
        {
            lock (quarantined) quarantined.Remove(resources); BubbleCountWindow.NativeUnavailable = previous;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_waits_for_slow_stop_or_the_four_second_budget(bool timeout)
    {
        var previous = BubbleCountWindow.NativeUnavailable; var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object(); using var release = new ManualResetEventSlim(); var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var delivered = 0;
        try
        {
            Assert.False(BubbleCountWindow.StopNative(() => release.Wait(), () => { }, () => { },
                resources, 30, action => action(), () => stopped.TrySetResult()));
            var completing = BubbleCountWindow.CompleteAfterStop(stopped.Task, TimeSpan.FromMilliseconds(3500),
                () => delivered++, _ => deadline.Task, action => action());
            Assert.False(completing.IsCompleted); Assert.Equal(0, delivered); // the two-second retry card must not consume a pending retry
            if (timeout) deadline.SetResult(); else release.Set();
            await completing.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(1, delivered);
            if (!timeout) Assert.True(stopped.Task.IsCompleted);
        }
        finally
        {
            release.Set();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            lock (quarantined) quarantined.Remove(resources); BubbleCountWindow.NativeUnavailable = previous;
        }
    }

    [Fact]
    public async Task Three_second_stop_defers_the_strict_result()
    {
        var previous = BubbleCountWindow.NativeUnavailable; using var release = new ManualResetEventSlim(); var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = 0;
        try
        {
            Assert.False(BubbleCountWindow.StopNative(() => release.Wait(), () => { }, () => { },
                new object(), 500, action => action(), () => settled.TrySetResult()));
            var result = BubbleCountWindow.CompleteAfterStop(settled.Task, TimeSpan.FromMilliseconds(3500),
                () => delivered++, post: action => action());
            await Task.Delay(2000); // the retry message would already have elapsed
            Assert.Equal(0, delivered);
            await Task.Delay(500); release.Set(); // three seconds after the stop began
            await result.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(1, delivered);
        }
        finally { release.Set(); await settled.Task.WaitAsync(TimeSpan.FromSeconds(3)); BubbleCountWindow.NativeUnavailable = previous; }
    }

    [Fact]
    public void Wedged_game_engine_cannot_be_the_shared_video_engine()
    {
        var property = typeof(LibVlcAudio).GetProperty("Shared", BindingFlags.Static | BindingFlags.NonPublic)!; var previous = LibVlcAudio.Shared;
        var factory = LibVlcAudio.VideoFactory; var previousUnavailable = BubbleCountWindow.NativeUnavailable; using var release = new ManualResetEventSlim();
        using var settled = new ManualResetEventSlim(); var shared = (LibVLC)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LibVLC));
        var isolated = (LibVLC)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LibVLC));
        GC.SuppressFinalize(shared); GC.SuppressFinalize(isolated);
        try
        {
            property.SetValue(null, shared);
            LibVlcAudio.VideoFactory = _ => isolated; Assert.Same(isolated, BubbleCountWindow.CreateGameEngine()); Assert.False(BubbleCountWindow.StopNative(() => release.Wait(), () => { }, () => { }, isolated, 30,
                action => action(), () => settled.Set()));
            Assert.Same(shared, LibVlcAudio.Shared); // subsequent video uses the untouched engine
            LibVlcAudio.VideoFactory = _ => shared; Assert.Throws<InvalidOperationException>(() => BubbleCountWindow.CreateGameEngine());
        }
        finally
        {
            release.Set(); settled.Wait(TimeSpan.FromSeconds(3)); BubbleCountWindow.NativeUnavailable = previousUnavailable;
            LibVlcAudio.VideoFactory = factory; property.SetValue(null, previous);
        }
    }

    [Fact]
    public async Task Shared_video_still_plays_while_an_isolated_game_stop_is_wedged()
    {
        try { _ = new LibVlcAudio("--aout=dummy", "--vout=dummy"); }
        catch (Exception ex)
        {
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CI") == "true") throw; Assert.Skip("libvlc unavailable: " + ex.Message);
        }
        var previous = BubbleCountWindow.NativeUnavailable; var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var game = BubbleCountWindow.CreateGameEngine(); var shared = LibVlcAudio.Shared!; Assert.NotSame(shared, game); using var release = new ManualResetEventSlim();
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Assert.False(BubbleCountWindow.StopNative(() => release.Wait(), () => game.Dispose(), () => { },
                game, 100, action => action(), () => settled.TrySetResult()));
            Assert.Same(shared, LibVlcAudio.Shared); using var media = new Media(shared, System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), FromType.FromPath);
            media.AddOption(":no-audio"); media.AddOption(":vout=dummy"); using var player = new MediaPlayer(media); var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Playing += (_, _) => playing.TrySetResult(); Assert.True(player.Play());
            await playing.Task.WaitAsync(TimeSpan.FromSeconds(15));
            player.Stop();
        }
        finally
        {
            release.Set();
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lock (quarantined) quarantined.Remove(game); BubbleCountWindow.NativeUnavailable = previous;
        }
    }

    [Fact]
    public void Pending_teardowns_each_hold_native_starts_until_ui_cleanup_finishes()
    {
        var previous = BubbleCountWindow.NativeUnavailable; var previousAvailable = BubbleCountHost.NativePlayerAvailable; BubbleCountHost.NativePlayerAvailable = () => true;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var first = new object(); var second = new object(); using var releaseFirst = new ManualResetEventSlim(); using var releaseSecond = new ManualResetEventSlim();
        using var firstPosted = new ManualResetEventSlim(); using var secondCleaned = new ManualResetEventSlim();
        Action? uiCleanup = null;
        try
        {
            BubbleCountWindow.NativeUnavailable = false; Assert.False(BubbleCountWindow.StopNative(() => releaseFirst.Wait(), () => { }, () => { }, first, 100,
                action => { uiCleanup = action; firstPosted.Set(); }));
            Assert.False(BubbleCountWindow.StopNative(() => releaseSecond.Wait(), () => { }, () => { }, second, 100,
                action => { action(); secondCleaned.Set(); }));
            releaseFirst.Set(); Assert.True(firstPosted.Wait(TimeSpan.FromSeconds(2))); Assert.True(BubbleCountWindow.NativeUnavailable); // UI cleanup is still queued
            Assert.Contains(first, quarantined); uiCleanup!(); Assert.DoesNotContain(first, quarantined); Assert.Contains(second, quarantined);
            Assert.True(BubbleCountWindow.NativeUnavailable); // the other stop is still running
            releaseSecond.Set(); Assert.True(secondCleaned.Wait(TimeSpan.FromSeconds(2))); Assert.False(BubbleCountWindow.NativeUnavailable);
            Assert.True(BubbleCountHost.Instance.CanPlayPath("next.mp4")); Assert.DoesNotContain(second, quarantined);
        }
        finally
        {
            releaseFirst.Set(); releaseSecond.Set();
            secondCleaned.Wait(TimeSpan.FromSeconds(2));
            lock (quarantined) { quarantined.Remove(first); quarantined.Remove(second); }
            BubbleCountWindow.NativeUnavailable = previous; BubbleCountHost.NativePlayerAvailable = previousAvailable;
        }
    }

    private sealed class RetryHost : IBubbleCountHost
    {
        public int Shows;
        public Action<bool>? Done;
        public Action? Retry;
        public bool CanPlayPath(string path) => BubbleCountHost.Instance.CanPlayPath(path);
        public void Show(string path, int difficulty, bool strict, Action<bool> complete) { Shows++; Done = complete; }
        public void ShowMessage(string text, int ms, Action then) => Retry = then;
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 1;
    }

    [Fact]
    public async Task Seven_second_stop_keeps_the_scheduled_strict_retry()
    {
        var oldClock = BubbleCountWindow.NativeClock; var oldAvailable = BubbleCountHost.NativePlayerAvailable; var oldStrict = CoreSettings.Current.BubbleCountStrictLock; var oldPause = CoreSettings.Current.BubbleCountPauseEffects;
        var oldUnavailable = BubbleCountWindow.NativeUnavailable; long now = 1000; using var release = new ManualResetEventSlim();
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var host = new RetryHost(); var scheduler = new BubbleCountScheduler(host, library: () => new[] { "next.mp4" });
        try
        {
            BubbleCountWindow.NativeClock = () => now; BubbleCountHost.NativePlayerAvailable = () => true; CoreSettings.Current.BubbleCountStrictLock = true; CoreSettings.Current.BubbleCountPauseEffects = false;
            scheduler.Trigger(true);
            await Task.Delay(1200); Assert.Equal(1, host.Shows); Assert.False(BubbleCountWindow.StopNative(() => release.Wait(), () => { }, () => { }, new object(), 20,
                action => action(), () => settled.TrySetResult()));
            now = 5000;
            await BubbleCountWindow.CompleteAfterStop(settled.Task, TimeSpan.Zero, () => host.Done!(false),
                _ => Task.CompletedTask, action => action());
            Assert.NotNull(host.Retry); now = 7000; host.Retry!();
            Assert.Equal(2, host.Shows); Assert.True(scheduler.IsBusy);
            now = 8000; release.Set(); await settled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            release.Set(); await settled.Task.WaitAsync(TimeSpan.FromSeconds(3)); scheduler.ForceCleanup(); BubbleCountWindow.NativeClock = oldClock; BubbleCountHost.NativePlayerAvailable = oldAvailable;
            BubbleCountWindow.NativeUnavailable = oldUnavailable; CoreSettings.Current.BubbleCountStrictLock = oldStrict; CoreSettings.Current.BubbleCountPauseEffects = oldPause;
        }
    }

    [Fact]
    public async Task Wedged_stop_expires_cooldown_without_releasing_its_graph()
    {
        var oldClock = BubbleCountWindow.NativeClock; var oldAvailable = BubbleCountHost.NativePlayerAvailable; var oldUnavailable = BubbleCountWindow.NativeUnavailable; long now = 1000;
        var quarantined = (List<object>)typeof(BubbleCountWindow).GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using var release = new ManualResetEventSlim(); var resources = new object(); var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BubbleCountWindow.NativeClock = () => now; BubbleCountHost.NativePlayerAvailable = () => true; Assert.False(BubbleCountWindow.StopNative(() => release.Wait(), () => { }, () => { }, resources, 20,
                action => action(), () => settled.TrySetResult()));
            now = 9749; Assert.True(BubbleCountHost.Instance.CanPlay);
            now = 9750; Assert.False(BubbleCountHost.Instance.CanPlay);
            now = 69749; Assert.False(BubbleCountHost.Instance.CanPlay);
            now = 69750; Assert.True(BubbleCountHost.Instance.CanPlay);
            Assert.Contains(resources, quarantined);
        }
        finally
        {
            release.Set(); await settled.Task.WaitAsync(TimeSpan.FromSeconds(3)); BubbleCountWindow.NativeClock = oldClock; BubbleCountHost.NativePlayerAvailable = oldAvailable;
            BubbleCountWindow.NativeUnavailable = oldUnavailable;
        }
    }

    [Fact]
    public void Isolated_engine_uses_the_app_audio_identity()
    {
        var engine = (LibVLC)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LibVLC)); GC.SuppressFinalize(engine);
        var options = new[] { "--aout=dummy" }; LibVLC? configured = null; string? name = null, agent = null;
        var result = LibVlcAudio.CreateVideoEngine(options, actual => { Assert.Same(options, actual); return engine; }, (actual, n, a) => { configured = actual; name = n; agent = a; });
        Assert.Same(engine, result); Assert.Same(engine, configured); Assert.Equal("Conditioning Control Panel", name); Assert.Equal("CCP", agent);
    }

}
