using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoSystemAwayTests
{
    private sealed class Host : IMandatoryVideoHost
    {
        public int Closed;
        public void Show(string path, bool strict) { }
        public double CloseAll() { Closed++; return 0; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    // WPF VideoService.cs:1843,1870: both strict and ordinary pre-roll count as active.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public Task LockAndSuspendCancelActivePreRoll(bool locked, bool strict) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var overlay = MandatoryVideoOverlay.Instance;
        var original = overlay.Scheduler;
        var host = new Host();
        var scheduler = new MandatoryVideoScheduler(host, library: () => new[] { "/v/test.mp4" });
        overlay.Scheduler = scheduler;
        try
        {
            Assert.True(scheduler.Trigger(strict));
            Assert.True(scheduler.IsPlaying);
            // Drive the actual Windows parser on Linux; logind uses these same two setters.
            var msg = locked ? ScrolllerAwayHost.Win32.WM_WTSSESSION_CHANGE : ScrolllerAwayHost.Win32.WM_POWERBROADCAST;
            var reason = locked ? ScrolllerAwayHost.Win32.WTS_SESSION_LOCK : ScrolllerAwayHost.Win32.PBT_APMSUSPEND;
            ScrolllerAwayHost.Win32.Handle(msg, (IntPtr)reason, IntPtr.Zero);
            Assert.True(scheduler.IsPlaying); // native callback queues, never tears down inline
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.False(scheduler.IsPlaying);
            Assert.Equal(1, host.Closed);
        }
        finally { scheduler.Stop(); overlay.Scheduler = original; ScrolllerAwayHost.Tracker.Reset(); }
    });

    [Fact]
    public Task UnlockResumeAndIdleLockDoNotCancelNewPlayback() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var overlay = MandatoryVideoOverlay.Instance;
        var original = overlay.Scheduler;
        var host = new Host();
        var scheduler = new MandatoryVideoScheduler(host, library: () => new[] { "/v/test.mp4" });
        overlay.Scheduler = scheduler;
        try
        {
            ScrolllerAwayHost.SetLocked(true); // idle: must not queue cleanup for the next video
            Assert.True(scheduler.Trigger());
            ScrolllerAwayHost.SetLocked(false);
            ScrolllerAwayHost.SetSleeping(false);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(scheduler.IsPlaying);
            Assert.Equal(0, host.Closed);
        }
        finally { scheduler.Stop(); overlay.Scheduler = original; ScrolllerAwayHost.Tracker.Reset(); }
    });
}
