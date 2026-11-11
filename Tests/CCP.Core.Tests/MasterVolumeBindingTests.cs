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
