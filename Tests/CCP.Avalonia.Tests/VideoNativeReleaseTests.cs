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
}
