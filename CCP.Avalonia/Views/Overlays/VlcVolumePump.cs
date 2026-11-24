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
        internal Task CloseAsync() => owner.Remove(this);
    }

    private readonly object _gate = new();
    private readonly List<Player> _players = new();
    private long _version;
    private int _volume;
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
        if (_running || _players.Count == 0) return;
        _running = true;
        _ = Task.Run(Drain);
    }

    private void Drain()
    {
        while (true)
        {
            Player? player = null;
            int volume;
            lock (_gate)
            {
                foreach (var candidate in _players)
                    if (candidate.AppliedVersion != _version) { player = candidate; break; }
                if (player == null) { _running = false; return; }
                player.AppliedVersion = _version;
                player.Busy = true;
                volume = _volume;
            }
            try { player.Apply(volume); }
            catch (Exception ex) { Log.Debug("Video volume update failed: {Error}", ex.Message); }
            finally
            {
                lock (_gate)
                {
                    player.Busy = false;
                    if (player.Removed) player.Closed.TrySetResult();
                }
            }
        }
    }

    // No native call holds the gate. A removed player cannot start another call.
    private Task Remove(Player player)
    {
        lock (_gate)
        {
            player.Removed = true;
            _players.Remove(player);
            if (!player.Busy) player.Closed.TrySetResult();
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
