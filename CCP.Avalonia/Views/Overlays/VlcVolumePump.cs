using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// All players share a Windows audio session, so one worker orders every volume write.
internal sealed class VlcVolumePump
{
    internal sealed class Player(VlcVolumePump owner, Action<int> apply)
    {
        internal readonly Action<int> Apply = apply;
        internal readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long AppliedVersion = -1;
        internal bool Busy, Removed;
        internal Action? Applied;
        internal Task CloseAsync() => owner.Remove(this);
        /// <summary>Apply the current volume again (LibVLC can ignore one set before Play, WPF VideoService.cs:4007-4015),
        /// then run <paramref name="applied"/> on the worker.</summary>
        internal void Reapply(Action? applied = null) => owner.Reapply(this, applied);
    }

    private readonly object _gate = new();
    private readonly List<Player> _players = new();
    private long _version;
    private int _volume;
    private int _worker;
    private bool _running;

    internal Player Add(Action<int> apply, int volume)
    {
        lock (_gate)
        {
            var player = new Player(this, apply);
            _players.Add(player);
            SetVolume(volume);
            return player;
        }
    }

    internal void Push(int value)
    {
        lock (_gate) SetVolume(value);
    }

    // Caller holds the gate; adding a player and publishing its current volume are atomic.
    private void SetVolume(int value)
    {
        _volume = Math.Max(0, value);
        _version++;
        Start();
    }

    private void Reapply(Player player, Action? applied)
    {
        lock (_gate)
        {
            if (player.Removed) return;
            player.AppliedVersion = -1;
            player.Applied += applied;
            Start();
        }
    }

    // Caller holds the gate.
    private void Start()
    {
        if (_running || _players.Count == 0) return;
        _running = true;
        var worker = _worker;
        _ = Task.Run(() => Drain(worker));
    }

    private void Drain(int worker)
    {
        while (true)
        {
            Player? player = null;
            int volume;
            Action? applied;
            lock (_gate)
            {
                if (worker != _worker) return;
                foreach (var candidate in _players)
                    if (candidate.AppliedVersion != _version) { player = candidate; break; }
                if (player == null) { _running = false; return; }
                player.AppliedVersion = _version;
                player.Busy = true;
                volume = _volume;
                applied = player.Applied;
                player.Applied = null;
            }
            try { player.Apply(volume); }
            catch (Exception ex) { Log.Debug("Video volume update failed: {Error}", ex.Message); }
            finally
            {
                lock (_gate)
                {
                    player.Busy = false;
                    if (player.Removed) player.Closed.TrySetResult();
                    if (worker != _worker)
                    {
                        // An abandoned call may land after newer writes on the shared session: write the latest again.
                        foreach (var other in _players) other.AppliedVersion = -1;
                        Start();
                    }
                }
                try { applied?.Invoke(); }   // a failed set still releases the hold, as WPF only logs it
                catch (Exception ex) { Log.Debug("Video volume callback failed: {Error}", ex.Message); }
            }
        }
    }

    // No native call holds the gate. A removed player cannot start another call, and a call stalled on it
    // is abandoned so the remaining players keep receiving volume.
    private Task Remove(Player player)
    {
        lock (_gate)
        {
            if (player.Removed) return player.Closed.Task;
            player.Removed = true;
            _players.Remove(player);
            if (!player.Busy) player.Closed.TrySetResult();
            else
            {
                _worker++;
                _running = false;
                Start();
            }
            return player.Closed.Task;
        }
    }
}

// Stop can run alongside a stalled volume call. Disposal and frame cleanup require both to finish.
internal static class VlcPlaybackRetirement
{
    private static readonly ConcurrentBag<object> Quarantine = new();

    internal static Task Retire(VlcVolumePump.Player? pump, Action stop, Action dispose, Action cleanup,
        Action<object>? quarantine = null)
    {
        var volumeFinished = pump?.CloseAsync() ?? Task.CompletedTask;
        var stopFinished = Task.Run(stop);
        return Finish();

        async Task Finish()
        {
            try
            {
                await Task.WhenAll(volumeFinished, stopFinished).ConfigureAwait(false);
                dispose();
                cleanup();
            }
            catch (Exception ex)
            {
                // Keep callbacks, player and frame buffer alive if native teardown failed.
                (quarantine ?? Quarantine.Add)((pump, stop, dispose, cleanup));
                Log.Debug("Video teardown failed: {Error}", ex.Message);
            }
        }
    }
}
