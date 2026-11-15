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
}
