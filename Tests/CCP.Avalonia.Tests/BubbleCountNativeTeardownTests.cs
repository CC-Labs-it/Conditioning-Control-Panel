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
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
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
            }, () => disposed = true, () => freed = true, resources, 250));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.False(BubbleCountHost.Instance.CanPlay);
            Assert.Contains(resources, quarantined);
            Assert.False(freed);
            Assert.False(disposed);
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(2)));
            Assert.Contains(resources, quarantined);
            Assert.False(freed);
            Assert.False(disposed);
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(2));
            quarantined.Remove(resources);
            BubbleCountWindow.NativeUnavailable = previous;
        }
    }
    [Fact]
    public void Stuck_disposal_also_quarantines_without_freeing_the_sink()
    {
        var previous = BubbleCountWindow.NativeUnavailable;
        var quarantined = (List<object>)typeof(BubbleCountWindow)
            .GetField("_quarantinedNative", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var resources = new object();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var freed = false;
        try
        {
            BubbleCountWindow.NativeUnavailable = false;
            Assert.False(BubbleCountWindow.StopNative(() => { }, () =>
            {
                try { release.Wait(); } finally { finished.Set(); }
            }, () => freed = true, resources, 250));
            Assert.True(BubbleCountWindow.NativeUnavailable);
            Assert.Contains(resources, quarantined);
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(freed);
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(2));
            quarantined.Remove(resources);
            BubbleCountWindow.NativeUnavailable = previous;
        }
    }

}
