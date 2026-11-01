using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountNativeTeardownTests
{
    [Fact]
    public void Settled_stop_runs_off_caller_before_freeing_the_sink()
    {
        var caller = Environment.CurrentManagedThreadId;
        var stopped = false;
        var disposed = false;
        var freed = false;
        Assert.True(BubbleCountWindow.StopNative(() =>
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
        var previous = BubbleCountWindow.NativeUnavailable;
        var previousAvailable = BubbleCountHost.NativePlayerAvailable;
        BubbleCountHost.NativePlayerAvailable = () => true;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        using var cleaned = new ManualResetEventSlim();
        var disposed = false;
        var freed = false;
        try
        {
            BubbleCountWindow.NativeUnavailable = false;
            var watch = Stopwatch.StartNew();
            Assert.False(BubbleCountWindow.StopNative(() =>
            {
                entered.Set();
                try { if (throws) throw new InvalidOperationException(); else release.Wait(); }
                finally { finished.Set(); }
            }, () => disposed = true, () => freed = true, resources, 500,
                action => { action(); cleaned.Set(); }));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.False(BubbleCountHost.Instance.CanPlay);
            Assert.Contains(resources, quarantined);
            Assert.False(freed);
            Assert.False(disposed);
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(2)));
            if (throws)
            {
                Assert.Contains(resources, quarantined);
                Assert.True(BubbleCountWindow.NativeUnavailable);
                Assert.False(freed);
                Assert.False(disposed);
            }
            else
            {
                Assert.True(cleaned.Wait(TimeSpan.FromSeconds(2)));
                Assert.True(disposed);
                Assert.True(freed);
                Assert.DoesNotContain(resources, quarantined);
                Assert.False(BubbleCountWindow.NativeUnavailable);
                Assert.True(BubbleCountHost.Instance.CanPlayPath("next.mp4"));
            }
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(2));
            if (!throws) cleaned.Wait(TimeSpan.FromSeconds(2));
            lock (quarantined) quarantined.Remove(resources);
            BubbleCountWindow.NativeUnavailable = previous;
            BubbleCountHost.NativePlayerAvailable = previousAvailable;
        }
    }
    [Fact]
    public void Stuck_disposal_also_quarantines_without_freeing_the_sink()
    {
        var previous = BubbleCountWindow.NativeUnavailable;
        var previousAvailable = BubbleCountHost.NativePlayerAvailable;
        BubbleCountHost.NativePlayerAvailable = () => true;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        using var cleaned = new ManualResetEventSlim();
        var freed = false;
        try
        {
            BubbleCountWindow.NativeUnavailable = false;
            Assert.False(BubbleCountWindow.StopNative(() => { }, () =>
            {
                try { release.Wait(); } finally { finished.Set(); }
            }, () => freed = true, resources, 500, action => { action(); cleaned.Set(); }));
            Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.Contains(resources, quarantined);
            Assert.False(freed);
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(cleaned.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(freed);
            Assert.DoesNotContain(resources, quarantined);
            Assert.False(BubbleCountWindow.NativeUnavailable);
            Assert.True(BubbleCountHost.Instance.CanPlayPath("next.mp4"));
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(2));
            cleaned.Wait(TimeSpan.FromSeconds(2));
            lock (quarantined) quarantined.Remove(resources);
            BubbleCountWindow.NativeUnavailable = previous;
            BubbleCountHost.NativePlayerAvailable = previousAvailable;
        }
    }

    [Fact]
    public void Failed_disposal_remains_quarantined()
    {
        var previous = BubbleCountWindow.NativeUnavailable;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object();
        var freed = false;
        try
        {
            Assert.False(BubbleCountWindow.StopNative(() => { }, () => throw new InvalidOperationException(),
                () => freed = true, resources, 2000, action => action()));
            Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.Contains(resources, quarantined);
            Assert.False(freed);
        }
        finally
        {
            lock (quarantined) quarantined.Remove(resources);
            BubbleCountWindow.NativeUnavailable = previous;
        }
    }

    [Fact]
    public void Failed_sink_cleanup_is_not_retried_and_stays_quarantined()
    {
        var previous = BubbleCountWindow.NativeUnavailable;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object();
        var calls = 0;
        try
        {
            Assert.False(BubbleCountWindow.StopNative(() => { }, () => { },
                () => { calls++; throw new InvalidOperationException(); }, resources, 2000, action => action()));
            Assert.Equal(1, calls);
            Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.Contains(resources, quarantined);
        }
        finally
        {
            lock (quarantined) quarantined.Remove(resources);
            BubbleCountWindow.NativeUnavailable = previous;
        }
    }

    [Fact]
    public void Pending_teardowns_each_hold_native_starts_until_ui_cleanup_finishes()
    {
        var previous = BubbleCountWindow.NativeUnavailable;
        var previousAvailable = BubbleCountHost.NativePlayerAvailable;
        BubbleCountHost.NativePlayerAvailable = () => true;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var first = new object(); var second = new object();
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        using var firstPosted = new ManualResetEventSlim();
        using var secondCleaned = new ManualResetEventSlim();
        Action? uiCleanup = null;
        try
        {
            BubbleCountWindow.NativeUnavailable = false;
            Assert.False(BubbleCountWindow.StopNative(() => releaseFirst.Wait(), () => { }, () => { }, first, 100,
                action => { uiCleanup = action; firstPosted.Set(); }));
            Assert.False(BubbleCountWindow.StopNative(() => releaseSecond.Wait(), () => { }, () => { }, second, 100,
                action => { action(); secondCleaned.Set(); }));
            releaseFirst.Set();
            Assert.True(firstPosted.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(BubbleCountWindow.NativeUnavailable); // UI cleanup is still queued
            Assert.Contains(first, quarantined);
            uiCleanup!();
            Assert.DoesNotContain(first, quarantined);
            Assert.Contains(second, quarantined);
            Assert.True(BubbleCountWindow.NativeUnavailable); // the other stop is still running
            releaseSecond.Set();
            Assert.True(secondCleaned.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(BubbleCountWindow.NativeUnavailable);
            Assert.True(BubbleCountHost.Instance.CanPlayPath("next.mp4"));
            Assert.DoesNotContain(second, quarantined);
        }
        finally
        {
            releaseFirst.Set(); releaseSecond.Set();
            secondCleaned.Wait(TimeSpan.FromSeconds(2));
            lock (quarantined) { quarantined.Remove(first); quarantined.Remove(second); }
            BubbleCountWindow.NativeUnavailable = previous;
            BubbleCountHost.NativePlayerAvailable = previousAvailable;
        }
    }

}
