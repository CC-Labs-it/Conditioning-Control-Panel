using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VlcVolumePumpTests
{
    [Fact]
    public async Task UpdatesRunOffCallerAndCollapseToLatest()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var values = new ConcurrentQueue<int>();
        var caller = Environment.CurrentManagedThreadId;
        var worker = caller;
        var pump = new VlcVolumePump();
        var player = pump.Add(value =>
        {
            worker = Environment.CurrentManagedThreadId;
            values.Enqueue(value);
            if (value == 1) { entered.Set(); release.Wait(); }
            if (value == 3) done.Set();
        }, 1);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            pump.Push(2);
            pump.Push(3);
        }
        finally { release.Set(); }
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        await player.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(caller, worker);
        Assert.Equal(new[] { 1, 3 }, values.ToArray());
    }

    [Fact]
    public async Task SessionOrdersMuteAndSliderChangesAcrossPlayersAndRetirement()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var secondCalled = new ManualResetEventSlim();
        var first = new ConcurrentQueue<int>();
        var second = new ConcurrentQueue<int>();
        var third = new ConcurrentQueue<int>();
        var pump = new VlcVolumePump();
        var primary = pump.Add(value =>
        {
            first.Enqueue(value);
            if (value == 70) { entered.Set(); release.Wait(); }
        }, 70);
        VlcVolumePump.Player? floating = null;
        VlcVolumePump.Player? joining = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            floating = pump.Add(value => { second.Enqueue(value); secondCalled.Set(); }, 70);
            Assert.False(secondCalled.Wait(TimeSpan.FromSeconds(2)));
            pump.Push(0);  // mute
            pump.Push(30); // a newer slider change
            pump.Push(0);  // mute again before the old call returns
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => first.Contains(0) && second.Contains(0), TimeSpan.FromSeconds(5)));
            Assert.Equal(new[] { 70, 0 }, first.ToArray());
            Assert.Equal(new[] { 0 }, second.ToArray());
            await primary.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            joining = pump.Add(third.Enqueue, 0); // a later window joins the still-muted run
            Assert.True(SpinWait.SpinUntil(() => third.Contains(0), TimeSpan.FromSeconds(5)));
            pump.Push(40);
            Assert.True(SpinWait.SpinUntil(() => second.Contains(40) && third.Contains(40), TimeSpan.FromSeconds(5)));
            Assert.Equal(new[] { 0, 40 }, second.Distinct().ToArray());
            Assert.Equal(new[] { 0, 40 }, third.Distinct().ToArray());
        }
        finally
        {
            release.Set();
            await primary.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (floating != null) await floating.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (joining != null) await joining.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task OverlayMuteAndSliderChangesReachEverySessionPlayer()
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var saved = (settings.MasterVolume, settings.VideoVolume);
        var overlay = new MandatoryVideoOverlay(null);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var pump = (VlcVolumePump)typeof(MandatoryVideoOverlay).GetField("_sessionVolume", flags)!.GetValue(overlay)!;
        var playing = typeof(ConditioningControlPanel.Services.MandatoryVideoScheduler).GetField("_playing", flags)!;
        var first = new ConcurrentQueue<int>();
        var second = new ConcurrentQueue<int>();
        var primary = pump.Add(first.Enqueue, 60);
        var floating = pump.Add(second.Enqueue, 60);
        try
        {
            (settings.MasterVolume, settings.VideoVolume) = (80, 100);
            playing.SetValue(overlay.Scheduler, true);
            overlay.ToggleUserMute();
            settings.MasterVolume = 35;
            overlay.UpdateVolume();
            Assert.True(overlay.UserMuted);
            Assert.Equal(0, overlay.PlayerVolume);
            Assert.True(SpinWait.SpinUntil(() => first.Contains(0) && second.Contains(0), TimeSpan.FromSeconds(5)));
            overlay.ToggleUserMute();
            Assert.True(SpinWait.SpinUntil(() => first.Contains(35) && second.Contains(35), TimeSpan.FromSeconds(5)));
        }
        finally
        {
            playing.SetValue(overlay.Scheduler, false);
            (settings.MasterVolume, settings.VideoVolume) = saved;
            await primary.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await floating.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task RetirementStartsStopWithoutWaitingForVolumeAndDefersDisposal()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        var calls = 0;
        var disposed = 0;
        var cleaned = 0;
        var pump = new VlcVolumePump();
        var player = pump.Add(_ => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(); }, 1);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task? retirement = null;
        try
        {
            // Run the entry point on a separate caller so a regression cannot hang the test host.
            var callReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(() =>
            {
                retirement = VlcPlaybackRetirement.Retire(player, stopped.Set,
                    () => Interlocked.Increment(ref disposed), () => Interlocked.Increment(ref cleaned));
                callReturned.SetResult();
            });
            await callReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(stopped.Wait(TimeSpan.FromSeconds(5)));
            pump.Push(2);
            Assert.Equal(0, Volatile.Read(ref disposed));
            Assert.Equal(0, Volatile.Read(ref cleaned));
            Assert.False(retirement!.IsCompleted);
        }
        finally { release.Set(); }
        await retirement!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(1, Volatile.Read(ref disposed));
        Assert.Equal(1, Volatile.Read(ref cleaned));
    }

    [Fact]
    public async Task RetirementWaitsForStopAndQuarantinesFailure()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var disposed = 0;
        var cleaned = 0;
        var retirement = VlcPlaybackRetirement.Retire(null, () => { entered.Set(); release.Wait(); },
            () => Interlocked.Increment(ref disposed), () => Interlocked.Increment(ref cleaned));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(retirement.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref disposed));
        }
        finally { release.Set(); }
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref disposed));
        Assert.Equal(1, Volatile.Read(ref cleaned));

        var quarantined = 0;
        await VlcPlaybackRetirement.Retire(null, () => throw new InvalidOperationException(),
            () => Interlocked.Increment(ref disposed), () => Interlocked.Increment(ref cleaned),
            _ => Interlocked.Increment(ref quarantined)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref quarantined));
        Assert.Equal(1, Volatile.Read(ref disposed));
        Assert.Equal(1, Volatile.Read(ref cleaned));
    }
}
