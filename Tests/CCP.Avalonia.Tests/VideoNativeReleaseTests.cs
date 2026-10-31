using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoNativeReleaseTests
{
    [Fact]
    public Task Hung_stop_leaves_dispatcher_free_and_keeps_buffers_until_native_disposal() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            using var unblock = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposed = false;
            var freed = false;
            var release = MandatoryVideoOverlay.ReleaseNativeAsync(() =>
            {
                entered.SetResult();
                unblock.Wait(TimeSpan.FromSeconds(5));
            }, () => disposed = true, () =>
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                Assert.True(disposed);
                freed = true;
            });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(release.IsCompleted);
                Assert.False(disposed);
                Assert.False(freed);
                var uiRan = false;
                Dispatcher.UIThread.Post(() => uiRan = true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(uiRan);
            }
            finally { unblock.Set(); }
            await release;
            Dispatcher.UIThread.RunJobs();
            Assert.True(freed);
        });

    [Fact]
    public Task Failed_stop_still_disposes_but_failed_dispose_never_frees_a_live_buffer() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var disposed = false;
            var freed = false;
            await MandatoryVideoOverlay.ReleaseNativeAsync(() => throw new InvalidOperationException(),
                () => disposed = true, () => freed = true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(disposed);
            Assert.True(freed);
            freed = false;
            await MandatoryVideoOverlay.ReleaseNativeAsync(() => { },
                () => throw new InvalidOperationException(), () => freed = true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(freed);
        });
    [Fact]
    public Task Replacement_waits_for_the_old_stop_without_blocking_the_dispatcher() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            using var unblock = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposed = false;
            var started = false;
            var release = MandatoryVideoOverlay.ReleaseNativeAsync(() =>
            {
                entered.SetResult();
                unblock.Wait(TimeSpan.FromSeconds(5));
            }, () => disposed = true, () => { });
            var replacement = MandatoryVideoOverlay.AfterReleaseAsync(release, () =>
            {
                Assert.True(disposed);
                started = true;
                return "original";
            }, () => throw new InvalidOperationException("early retirement"), TimeSpan.FromSeconds(2));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(started);
                Assert.False(replacement.IsCompleted);
                var pumped = false;
                Dispatcher.UIThread.Post(() => pumped = true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(pumped);
            }
            finally { unblock.Set(); }
            Assert.Equal("original", await replacement);
            Assert.True(started);
        });

    [Fact]
    public async Task Wedged_stop_uses_fresh_instance_and_wedged_recovery_is_bounded()
    {
        var stuck = new TaskCompletionSource<object>();
        var fresh = new object();
        var budget = TimeSpan.FromMilliseconds(20);
        var replacement = await MandatoryVideoOverlay.AfterReleaseAsync(stuck.Task,
            () => throw new InvalidOperationException("poisoned instance reused"), () => Task.FromResult(fresh), budget);
        Assert.Same(fresh, replacement);
        Assert.False(stuck.Task.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(() => MandatoryVideoOverlay.AfterReleaseAsync(stuck.Task,
            () => new object(), () => stuck.Task, budget));
    }

    [Fact]
    public Task Stop_cancels_a_replacement_waiting_for_native_release() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var overlay = MandatoryVideoOverlay.Instance;
            var saved = overlay.NativeRelease;
            var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                overlay.NativeRelease = stopping.Task;
                var replacement = overlay.PlayAsync("unused.mp4", false);
                Assert.False(replacement.IsCompleted);
                Assert.Empty(overlay.Surfaces);
                overlay.CloseAll();
                stopping.SetResult();
                await replacement;
                Assert.Empty(overlay.Surfaces);
            }
            finally
            {
                stopping.TrySetResult();
                overlay.CloseAll();
                overlay.NativeRelease = saved;
            }
        });

}
