using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using Xunit;
namespace CCP.Core.Tests;
public sealed class MasterVolumeBindingTests
{
    [Theory, InlineData(0, 1, 0), InlineData(40, 0.5, 0.2), InlineData(200, 2, 1), InlineData(100, double.NaN, 0)]
    public void Mind_wipe_gain_is_linear_and_clamped(int master, double own, double expected)
        => Assert.Equal(expected, MasterVolumeBinding.MindWipeGain(master, own), 5);
    [Fact]
    public void Watch_follows_replacement_detaches_old_settings_and_disposes()
    {
        var service = new SettingsService(); var original = service.Current;
        var levels = new List<int>();
        try
        {
            using var watch = new MasterVolumeBinding(levels.Add, service);
            original.MasterVolume = 37;
            Assert.Equal(37, levels[^1]);
            var replacement = new AppSettings { MasterVolume = 23 };
            service.RestoreFrom(replacement);
            Assert.Equal(23, levels[^1]);
            var count = levels.Count; original.MasterVolume = 72;
            Assert.Equal(count, levels.Count);
            replacement.MasterVolume = 0;
            Assert.Equal(0, levels[^1]); watch.Dispose();
            count = levels.Count;
            replacement.MasterVolume = 55;
            Assert.Equal(count, levels.Count);
        }
        finally { service.RestoreFrom(original); }
    }
    [Fact]
    public void Replacement_racing_construction_is_never_missed()
    {
        var service = new SettingsService(); var original = service.Current;
        var go = 0;
        var worker = new Thread(() => { while (Volatile.Read(ref go) >= 0) if (Interlocked.CompareExchange(ref go, 2, 1) == 1) { service.Reset(); go = 0; } });
        worker.Start();
        try
        {
            for (var i = 0; i < 4000; i++)   // the delay sweep walks construction across the instant Reset swaps Current
            {
                service.Current.MasterVolume = 0;
                Volatile.Write(ref go, 1);
                Thread.SpinWait(i % 400 * 10);
                using var watch = new MasterVolumeBinding(_ => { }, service);
                SpinWait.SpinUntil(() => Volatile.Read(ref go) == 0);
                Assert.Equal(service.Current.MasterVolume, watch.Level);
            }
        }
        finally { Volatile.Write(ref go, -1); worker.Join(); service.RestoreFrom(original); }
    }
    [Fact]
    public void Binding_built_while_the_handler_list_is_taken_still_sees_the_swap()
    {
        // Holds Reset where a reordered store would leave it: handler list already read, new
        // settings not yet visible. A binding built then must wait for the swap, not miss it.
        var service = new SettingsService(); var original = service.Current;
        original.MasterVolume = 0;
        MasterVolumeBinding? built = null;
        using var done = new ManualResetEventSlim();
        Thread? builder = null;
        service.ReplacingSeam = () =>
        {
            // A thread, not a Task: Task.Wait may inline the work here, inside the gate.
            builder = new Thread(() => { built = new MasterVolumeBinding(_ => { }, service); done.Set(); });
            builder.Start();
            done.Wait(TimeSpan.FromMilliseconds(500));   // finishes here only if it skipped the gate
        };
        try
        {
            service.Reset();
            builder!.Join();
            using var watch = built!;
            Assert.NotEqual(0, service.Current.MasterVolume);
            Assert.Equal(service.Current.MasterVolume, watch.Level);
        }
        finally { service.ReplacingSeam = null; service.RestoreFrom(original); }
    }
    [Fact]
    public void Replacement_in_flight_at_disposal_does_not_reattach()
    {
        var service = new SettingsService(); var original = service.Current;
        using ManualResetEventSlim entered = new(), release = new();
        void Hold() { entered.Set(); release.Wait(); }
        service.CurrentReplaced += Hold;   // runs first; the binding's handler is already in the snapshot
        [MethodImpl(MethodImplOptions.NoInlining)]
        WeakReference DisposeMidReplacement()
        {
            var watch = new MasterVolumeBinding(_ => { }, service);
            var reset = Task.Run(service.Reset); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            watch.Dispose(); release.Set();
            reset.Wait();
            return new WeakReference(watch);
        }
        try
        {
            var weak = DisposeMidReplacement();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(weak.IsAlive);
        }
        finally { service.CurrentReplaced -= Hold; release.Set(); service.RestoreFrom(original); }
    }
}
