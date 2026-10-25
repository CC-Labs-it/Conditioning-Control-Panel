using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// The app-wide budget for silent clip players (flashes, bubbles, any other non-flash clip). It counts
/// LIVE LEASES, never a bare counter: a lease is in the set or it is not, so a missed or doubled
/// release cannot drift the count. Releasing a lease twice is a no-op.
///
/// When every slot is taken, the oldest lease its owner marked <see cref="Lease.Evictable"/>
/// (a flash fading out) gives its slot to the new request instead of the
/// request falling back to a still. The evicted owner hears about it through its callback.
///
/// Flashes hold RESERVED leases. Every other surface (bubbles, any other non-flash clip) shares
/// <see cref="SharedCap"/> of the slots, so a burst of bubbles can never leave the flashes with
/// nothing but posters.
/// Thread-safe; the eviction callback runs outside the lock on the acquiring thread.
/// </summary>
internal sealed class ClipSlotBudget
{
    private readonly object _gate = new();
    private readonly List<Lease> _live = new();

    public ClipSlotBudget(int capacity, int sharedCap = int.MaxValue)
    {
        Capacity = Math.Max(1, capacity);
        SharedCap = Math.Clamp(sharedCap, 1, Capacity);
    }

    public int Capacity { get; }

    /// <summary>Most slots the non-reserved (non-flash) leases may hold together.</summary>
    public int SharedCap { get; }

    public int Live { get { lock (_gate) return _live.Count; } }

    public int LiveShared
    {
        get
        {
            lock (_gate)
            {
                int shared = 0;
                for (int i = 0; i < _live.Count; i++) if (!_live[i].Reserved) shared++;
                return shared;
            }
        }
    }

    /// <summary>True when a shared acquire right now would succeed (a free slot or an evictable one).</summary>
    public bool CanAcquire => CanAcquireFor(reserved: false);

    public bool CanAcquireFor(bool reserved)
    {
        lock (_gate) return VictimIndex(reserved) is not null;
    }

    /// <summary>A lease, or null when every slot is held and none is evictable.</summary>
    public Lease? TryAcquire(Action? onEvicted = null) => TryAcquire(onEvicted, out _);

    public Lease? TryAcquire(Action? onEvicted, out Lease? evicted, bool reserved = false)
    {
        evicted = null;
        Lease granted;
        lock (_gate)
        {
            if (VictimIndex(reserved) is not { } victim) return null;
            if (victim >= 0)
            {
                evicted = _live[victim];
                _live.RemoveAt(victim);
            }
            granted = new Lease(this, onEvicted, reserved);
            _live.Add(granted);
        }
        if (evicted != null)
        {
            try { evicted.OnEvicted?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("Clip slot eviction failed: {Error}", ex.Message); }
        }
        return granted;
    }

    /// <summary>Under the lock: null = refuse, -1 = a free slot, otherwise the lease to evict.
    /// _live is in acquire order, so the first evictable match is the oldest.</summary>
    private int? VictimIndex(bool reserved)
    {
        // Plain loops: this runs on every refused heartbeat tick, so no capturing lambdas.
        int shared = 0;
        for (int i = 0; i < _live.Count; i++) if (!_live[i].Reserved) shared++;
        bool sharedFull = !reserved && shared >= SharedCap;
        if (!sharedFull && _live.Count < Capacity) return -1;
        // A shared lease over its sub-cap may only take a slot from another shared lease.
        for (int i = 0; i < _live.Count; i++)
        {
            var l = _live[i];
            if (l.Evictable && (!sharedFull || !l.Reserved)) return i;
        }
        return null;
    }

    public void StopAll()
    {
        Lease[] leases;
        lock (_gate) { leases = _live.ToArray(); _live.Clear(); }
        foreach (var lease in leases)
        {
            try { lease.OnEvicted?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("Clip stop failed: {Error}", ex.Message); }
        }
    }

    private bool Release(Lease lease)
    {
        lock (_gate) return _live.Remove(lease);
    }

    internal sealed class Lease : IDisposable
    {
        private readonly ClipSlotBudget _owner;

        internal Lease(ClipSlotBudget owner, Action? onEvicted, bool reserved)
        {
            _owner = owner;
            OnEvicted = onEvicted;
            Reserved = reserved;
        }

        internal Action? OnEvicted { get; }

        /// <summary>A flash's lease: not counted against <see cref="SharedCap"/>.</summary>
        public bool Reserved { get; }

        /// <summary>The owner no longer needs this slot much (its picture is leaving).</summary>
        public volatile bool Evictable;

        /// <summary>True when this call gave the slot back; false when it was already free.</summary>
        public bool Release() => _owner.Release(this);

        public void Dispose() => Release();
    }
}
