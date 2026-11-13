using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

// Owner, 2026-10-19: "after a few rounds none [of the GIF flashes] animate". The clip player
// budget must pair every acquire with a release, and the clip files must outlive their draw.
public class FlashClipSlotTests
{
    [Fact]
    public void BudgetRefusesAtCapAndReleaseFreesTheSlot()
    {
        var budget = new ClipSlotBudget(8);
        var leases = Enumerable.Range(0, 8).Select(_ => budget.TryAcquire()).ToList();
        Assert.All(leases, l => Assert.NotNull(l));
        Assert.Equal(8, budget.Live);
        Assert.False(budget.CanAcquire);
        Assert.Null(budget.TryAcquire());

        Assert.True(leases[3]!.Release());
        Assert.False(leases[3]!.Release());   // a second release is a no-op, never a drift
        leases[3]!.Dispose();
        Assert.Equal(7, budget.Live);
        Assert.True(budget.CanAcquire);
        Assert.NotNull(budget.TryAcquire());
        Assert.Equal(8, budget.Live);
    }

    [Fact]
    public void FullBudgetEvictsTheOldestLeavingPlayerInsteadOfAStill()
    {
        var budget = new ClipSlotBudget(3);
        var evicted = new List<int>();
        var a = budget.TryAcquire(() => evicted.Add(0))!;
        var b = budget.TryAcquire(() => evicted.Add(1))!;
        var c = budget.TryAcquire(() => evicted.Add(2))!;
        Assert.Null(budget.TryAcquire());

        c.Evictable = true;
        b.Evictable = true;
        var d = budget.TryAcquire(null, out var victim);
        Assert.NotNull(d);
        Assert.Same(b, victim);               // oldest evictable, not the newest
        Assert.Equal(new[] { 1 }, evicted);
        Assert.Equal(3, budget.Live);
        Assert.False(b.Release());            // the owner's own dispose after eviction changes nothing
        Assert.Equal(3, budget.Live);
        Assert.False(a.Evictable);
    }

    [Fact]
    public void BubblesAndCascadeShareASubCapSoFlashesAlwaysHaveRoom()
    {
        var budget = new ClipSlotBudget(8, 5);
        var shared = Enumerable.Range(0, 5).Select(_ => budget.TryAcquire()).ToList();
        Assert.All(shared, l => Assert.NotNull(l));
        Assert.False(budget.CanAcquireFor(reserved: false));
        Assert.Null(budget.TryAcquire());                 // a sixth bubble face is refused
        Assert.True(budget.CanAcquireFor(reserved: true));
        var flashes = Enumerable.Range(0, 3).Select(i => budget.TryAcquire(null, out _, reserved: true)).ToList();
        Assert.All(flashes, l => Assert.NotNull(l));
        Assert.Equal(8, budget.Live);
        Assert.Equal(5, budget.LiveShared);

        // A leaving flash may lend its slot to another flash, but a bubble over its sub-cap
        // only ever takes a slot from another shared lease.
        flashes[0]!.Evictable = true;
        Assert.Null(budget.TryAcquire());
        shared[2]!.Evictable = true;
        Assert.NotNull(budget.TryAcquire(null, out var victim));
        Assert.Same(shared[2], victim);
        Assert.NotNull(budget.TryAcquire(null, out victim, reserved: true));
        Assert.Same(flashes[0], victim);
        Assert.Equal(5, budget.LiveShared);
    }

    [Fact]
    public void EvictionDisposesTheOwnerAndItsOwnReleaseDoesNotFreeASecondSlot()
    {
        // FlashClipPlayer's eviction callback is "created?.Dispose()", and Dispose releases the
        // lease. The lease is already gone by then: that release must not free another slot.
        var budget = new ClipSlotBudget(2);
        FakePlayer? created = null;
        var lease = budget.TryAcquire(() => created?.Dispose())!;
        created = new FakePlayer(lease);
        var other = budget.TryAcquire()!;
        created.Lease.Evictable = true;

        var taker = budget.TryAcquire(null, out var victim);
        Assert.NotNull(taker);
        Assert.Same(lease, victim);
        Assert.True(created.Disposed);
        Assert.False(created.ReleasedASlot);
        Assert.Equal(2, budget.Live);
        Assert.Null(budget.TryAcquire());
        other.Dispose();
        taker!.Dispose();
        Assert.Equal(0, budget.Live);
    }

    private sealed class FakePlayer : IDisposable
    {
        public FakePlayer(ClipSlotBudget.Lease lease) => Lease = lease;
        public ClipSlotBudget.Lease Lease { get; }
        public bool Disposed { get; private set; }
        public bool ReleasedASlot { get; private set; }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            ReleasedASlot = Lease.Release();
        }
    }

    // ---- the heartbeat's clip decision (FlashClipUpgrade) ----------------------------------

    private sealed class SimFlash
    {
        public bool Showing = true, Attempted, RefusedAtCap;
        public FakePlayer? Player;
    }

    /// <summary>One heartbeat tick for one flash, exactly as FlashService runs it.</summary>
    private static void Tick(SimFlash f, ClipSlotBudget budget)
    {
        if (f.Player is { } p)
        {
            p.Lease.Evictable = FlashClipUpgrade.Evictable(f.Showing);
            if (FlashClipUpgrade.Reclaim(true, p.Disposed, f.Showing))
            { f.Player = null; f.RefusedAtCap = true; f.Attempted = false; }
        }
        if (!FlashClipUpgrade.ShouldTryStart(true, true, f.Showing, f.Player != null, f.Attempted,
                f.RefusedAtCap, f.RefusedAtCap && budget.CanAcquireFor(reserved: true)))
            return;
        FakePlayer? created = null;
        var lease = budget.TryAcquire(() => created?.Dispose(), out _, reserved: true);
        if (lease != null) f.Player = created = new FakePlayer(lease);
        (f.Attempted, f.RefusedAtCap) = FlashClipUpgrade.AfterTry(lease != null, lease == null);
    }

    [Fact]
    public void PosterUpgradesToClipWhenASlotFrees()
    {
        var budget = new ClipSlotBudget(8, 5);
        var flashes = Enumerable.Range(0, 12).Select(_ => new SimFlash()).ToList();
        foreach (var f in flashes) Tick(f, budget);
        Assert.Equal(8, flashes.Count(f => f.Player != null));
        Assert.Equal(4, flashes.Count(f => f.RefusedAtCap && !f.Attempted));

        // Two clips close: the next tick upgrades two posters, the rest wait without a final no.
        flashes[0].Player!.Dispose(); flashes.RemoveAt(0);
        flashes[0].Player!.Dispose(); flashes.RemoveAt(0);
        foreach (var f in flashes) Tick(f, budget);
        Assert.Equal(8, flashes.Count(f => f.Player is { Disposed: false }));
        Assert.Equal(2, flashes.Count(f => f.RefusedAtCap));
    }

    [Fact]
    public void LeavingFlashLendsItsSlotAndALingerStartsOver()
    {
        var budget = new ClipSlotBudget(1);
        var leaving = new SimFlash();
        Tick(leaving, budget);
        Assert.NotNull(leaving.Player);

        var waiting = new SimFlash();
        Tick(waiting, budget);
        Assert.True(waiting.RefusedAtCap);

        leaving.Showing = false;                      // its fade starts
        Tick(leaving, budget);
        Assert.True(leaving.Player!.Lease.Evictable);
        Tick(waiting, budget);                        // takes the leaving slot
        Assert.NotNull(waiting.Player);
        Assert.True(leaving.Player!.Disposed);

        leaving.Showing = true;                       // a linger brings it back
        Tick(leaving, budget);
        Assert.Null(leaving.Player);
        Assert.True(leaving.RefusedAtCap);
        Assert.False(leaving.Attempted);
        Assert.Equal(1, budget.Live);
    }

    [Fact]
    public void FailedStartIsFinalAndCapRefusalIsNot()
    {
        Assert.Equal((true, false), FlashClipUpgrade.AfterTry(started: true, atCap: false));
        Assert.Equal((true, false), FlashClipUpgrade.AfterTry(started: false, atCap: false));
        Assert.Equal((false, true), FlashClipUpgrade.AfterTry(started: false, atCap: true));
        Assert.False(FlashClipUpgrade.ShouldTryStart(true, true, showing: false, false, false, false, true));
        Assert.False(FlashClipUpgrade.ShouldTryStart(true, true, true, false, false, refusedAtCap: true, canStart: false));
        Assert.True(FlashClipUpgrade.ShouldTryStart(true, true, true, false, false, refusedAtCap: true, canStart: true));
    }

    [Fact]
    public void ThousandRoundsOfTicksNeverStrandAPoster()
    {
        var budget = new ClipSlotBudget(8, 5);
        var rng = new Random(2491);
        for (int round = 0; round < 1000; round++)
        {
            var flashes = Enumerable.Range(0, 20).Select(_ => new SimFlash()).ToList();
            for (int t = 0; t < 30; t++)
            {
                // Two passes: a flash marked leaving late in pass one is lent in pass two.
                foreach (var f in flashes) Tick(f, budget);
                foreach (var f in flashes) Tick(f, budget);
                Assert.True(budget.Live <= 8);
                // A showing flash on its poster only ever waits while no slot can be had.
                if (flashes.Any(f => f.Showing && f.Player is null or { Disposed: true }))
                    Assert.False(budget.CanAcquireFor(reserved: true));
                // Flashes leave one by one; a few linger back.
                var live = flashes.Where(f => f.Showing).ToList();
                if (live.Count > 0) live[rng.Next(live.Count)].Showing = false;
                if (rng.Next(5) == 0 && flashes.FirstOrDefault(f => !f.Showing) is { } back) back.Showing = true;
            }
            foreach (var f in flashes) f.Player?.Dispose();   // round end: SafeCloseFlashWindow
            Assert.Equal(0, budget.Live);
        }
    }

    [Fact]
    public void ReleaseOnExceptionPathKeepsTheCountExact()
    {
        var budget = new ClipSlotBudget(8);
        for (int i = 0; i < 100; i++)
        {
            var lease = budget.TryAcquire()!;
            try { throw new InvalidOperationException("Clip playback refused"); }
            catch (InvalidOperationException) { lease.Dispose(); }
        }
        Assert.Equal(0, budget.Live);
    }

    [Fact]
    public void ThousandRoundsOfFlashesNeverDriftTheCount()
    {
        var budget = new ClipSlotBudget(8);
        var rng = new Random(1019);
        var live = new List<ClipSlotBudget.Lease>();
        int evictions = 0;
        for (int round = 0; round < 1000; round++)
        {
            // 20 flashes a round, as in the owner's log; some close before their start completes.
            for (int f = 0; f < 20; f++)
            {
                ClipSlotBudget.Lease? self = null;
                var lease = budget.TryAcquire(() => { evictions++; live.Remove(self!); });
                self = lease;
                if (lease == null) continue;
                if (rng.Next(10) == 0) { lease.Dispose(); continue; }   // closed early
                live.Add(lease);
                if (rng.Next(4) == 0) lease.Evictable = true;            // fading out
            }
            Assert.True(budget.Live <= budget.Capacity);
            Assert.Equal(live.Count, budget.Live);
            // Round end: every flash closes, some through two paths at once.
            foreach (var lease in live.ToList())
            {
                lease.Dispose();
                if (rng.Next(3) == 0) lease.Dispose();
            }
            live.Clear();
            Assert.Equal(0, budget.Live);
            Assert.True(budget.CanAcquire);
        }
        Assert.True(evictions > 0);
    }

}
