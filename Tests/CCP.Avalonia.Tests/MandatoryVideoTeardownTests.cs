using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MandatoryVideoTeardownTests
{
    private sealed class Decoder : IMandatoryPlayback
    {
        internal readonly ManualResetEventSlim Started = new(), Release = new(), Disposed = new();
        public long Time { get; set; }
        public long Length => 60000;
        public bool IsPlaying => true;
        public bool IsSeekable => false;
        public bool HasVideo => true;
        public void SetPause(bool paused) { }
        public void Stop() { Started.Set(); Release.Wait(); }
        public void Dispose() => Disposed.Set();
    }

    [Fact]
    public void CloseDoesNotWaitForStopAndDisposesOnlyAfterItReturns()
    {
        var decoder = new Decoder();
        // Ensure a synchronous regression eventually unblocks and fails the elapsed assertion.
        _ = Task.Run(async () => { await Task.Delay(2000); decoder.Release.Set(); });
        AvaloniaTestDispatcher.Run(() =>
        {
            var overlay = new MandatoryVideoOverlay(() => decoder);
            try
            {
                overlay.Show("fake.mp4", false);
                var clock = Stopwatch.StartNew();
                overlay.CloseAll();
                Assert.True(clock.ElapsedMilliseconds < 100, $"CloseAll took {clock.ElapsedMilliseconds} ms");
                Assert.True(decoder.Started.Wait(TimeSpan.FromSeconds(1)));
                Assert.False(decoder.Disposed.IsSet);
                decoder.Release.Set();
                Assert.True(decoder.Disposed.Wait(TimeSpan.FromSeconds(2)));
            }
            finally { decoder.Release.Set(); }
        });
    }

    [Fact]
    public void OverdueStopIsQuarantinedAndBlocksBubbleCountForOneMinute()
    {
        var decoder = new Decoder();
        var oldClock = BubbleCountWindow.NativeClock;
        long now = 1000;
        BubbleCountWindow.NativeClock = () => Interlocked.Read(ref now);
        BubbleCountWindow.ClearMandatoryNativePoisoningForTests();
        try
        {
            AvaloniaTestDispatcher.Run(() =>
            {
                var overlay = new MandatoryVideoOverlay(() => decoder) { StopGraceMs = 50 };
                overlay.Show("fake.mp4", false);
                overlay.CloseAll();
                Assert.True(decoder.Started.Wait(TimeSpan.FromSeconds(1)));
                Assert.True(SpinWait.SpinUntil(() => BubbleCountWindow.NativeBlocked, TimeSpan.FromSeconds(2)));
                decoder.Release.Set();
                Assert.False(decoder.Disposed.Wait(TimeSpan.FromMilliseconds(150)));
                Interlocked.Exchange(ref now, 60999);
                Assert.True(BubbleCountWindow.NativeBlocked);
                Interlocked.Exchange(ref now, 61000);
                Assert.False(BubbleCountWindow.NativeBlocked);
            });
        }
        finally
        {
            decoder.Release.Set();
            BubbleCountWindow.ClearMandatoryNativePoisoningForTests();
            BubbleCountWindow.NativeClock = oldClock;
        }
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        var limit = Stopwatch.StartNew();
        while (!condition() && limit.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(10);
        Assert.True(condition(), "Timed out waiting for playback");
    }

    [Fact]
    public Task NextClipWaitsForStopWithoutBlockingDispatcher() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var first = new Decoder();
        var second = new Decoder();
        var count = 0;
        var replaced = 0;
        var overlay = new MandatoryVideoOverlay(() => ++count == 1 ? first : second)
        { PendingStopWaitMs = 1500, ReplaceEngineForTests = () => { replaced++; return true; } };
        try
        {
            overlay.Show("one.mp4", false);
            overlay.CloseAll();
            Assert.True(first.Started.Wait(TimeSpan.FromSeconds(1)));
            overlay.Show("two.mp4", false);
            await Task.Delay(100); // dispatcher remains responsive while the stop is blocked
            Assert.Equal(1, count);
            first.Release.Set();
            await WaitUntil(() => count == 2);
            Assert.Equal(0, replaced);
        }
        finally { first.Release.Set(); second.Release.Set(); overlay.CloseAll(); }
    });

    [Fact]
    public Task TimedOutStopReplacesEngineBeforeConstructingPlayer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var first = new Decoder();
        var second = new Decoder();
        var count = 0;
        var replacements = 0;
        var replacedWhenBuilt = -1;
        var overlay = new MandatoryVideoOverlay(() =>
        {
            count++;
            if (count == 2) replacedWhenBuilt = replacements;
            return count == 1 ? first : second;
        }) { PendingStopWaitMs = 100, ReplaceEngineForTests = () => { replacements++; return true; } };
        try
        {
            overlay.Show("one.mp4", false);
            overlay.CloseAll();
            Assert.True(first.Started.Wait(TimeSpan.FromSeconds(1)));
            overlay.Show("two.mp4", false);
            Assert.Equal(1, count);
            await WaitUntil(() => count == 2);
            Assert.Equal(1, replacements);
            Assert.Equal(1, replacedWhenBuilt);
            Assert.False(first.Release.IsSet);
        }
        finally { first.Release.Set(); second.Release.Set(); overlay.CloseAll(); }
    });

    [Fact]
    public Task PendingPlaybackIsCancelledByCloseAndCannotStartOnFailedReplacement() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var first = new Decoder();
        var count = 0;
        var attempts = 0;
        var overlay = new MandatoryVideoOverlay(() => { count++; return first; })
        { PendingStopWaitMs = 80, ReplaceEngineForTests = () => { attempts++; return false; } };
        try
        {
            overlay.Show("one.mp4", false);
            overlay.CloseAll();
            overlay.Show("cancelled.mp4", false);
            overlay.CloseAll();
            await Task.Delay(150);
            Assert.Equal(1, count);
            Assert.Equal(0, attempts);
            overlay.Show("blocked.mp4", false);
            await WaitUntil(() => attempts == 1);
            Assert.Equal(1, count);
            first.Release.Set();
            await Task.Delay(100);
            overlay.Show("still-blocked.mp4", false);
            await WaitUntil(() => attempts == 2);
            Assert.Equal(1, count);
        }
        finally { first.Release.Set(); overlay.CloseAll(); }
    });

    [Fact]
    public void RetiredPresentationCallbacksCannotTouchNewClip() => AvaloniaTestDispatcher.Run(() =>
    {
        var decoder = new Decoder();
        decoder.Release.Set();
        var overlay = new MandatoryVideoOverlay(() => decoder);
        try
        {
            overlay.Show("one.mp4", false);
            var frames = 0;
            var bitmaps = 0;
            var oldFrame = overlay.GuardPresentation(() => frames++);
            var oldBitmap = overlay.GuardPresentation<int>(value => bitmaps += value);
            oldFrame(); oldBitmap(1);
            Assert.Equal(1, frames);
            Assert.Equal(1, bitmaps);
            overlay.CloseAll();
            overlay.Show("two.mp4", false);
            oldFrame(); oldBitmap(1);
            Assert.Equal(1, frames);
            Assert.Equal(1, bitmaps);
            overlay.GuardPresentation(() => frames++)();
            Assert.Equal(2, frames);
        }
        finally { overlay.CloseAll(); }
    });

}
