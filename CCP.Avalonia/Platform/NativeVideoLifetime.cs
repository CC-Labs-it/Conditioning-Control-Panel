using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Platform;

// Head-owned: Core supplies health decisions, but never owns native players or timers.
internal sealed class NativeVideoSafety(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly List<object> _quarantine = new();
    private long? _poisonedAt;
    internal bool CoolingDown { get { lock (_gate) return _poisonedAt is { } at && clock.GetElapsedTime(at) < TimeSpan.FromSeconds(60); } }
    internal int QuarantinedCount { get { lock (_gate) return _quarantine.Count; } }
    internal void Poison() { lock (_gate) _poisonedAt = clock.GetTimestamp(); }
    internal void Quarantine(object resources)
    {
        // ponytail: deliberately root the whole native graph for process lifetime, even if Stop
        // eventually returns. Reusing or disposing a player after a native wedge is unsafe.
        lock (_gate) { _quarantine.Add(resources); Poison(); }
    }
}

/// <summary>Armed before native startup, alive through teardown. All native work stays off the UI.</summary>
internal sealed class NativeVideoLifetime
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly NativeVideoSafety _safety;
    private readonly Action _timeout;
    private readonly ITimer _watch;
    private long _heartbeat;
    private bool _expired, _released;
    private Task _startup = Task.CompletedTask;
    private Task? _stop;
    internal bool Expired { get { lock (_gate) return _expired; } }
    internal bool Armed { get { lock (_gate) return !_released; } }

    internal NativeVideoLifetime(NativeVideoSafety safety, Action timeout, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _safety = safety;
        _timeout = timeout;
        _heartbeat = _clock.GetTimestamp();
        _watch = _clock.CreateTimer(_ => CheckHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    internal Task RunNative(Action operation)
    {
        lock (_gate)
        {
            if (_stop != null) return Task.CompletedTask;
            var previous = _startup;
            return _startup = Task.Run(async () => { await previous.ConfigureAwait(false); operation(); });
        }
    }

    // Called only after startup, by the UI heartbeat. Startup itself gets the same 8s WPF budget.
    internal void Heartbeat() { lock (_gate) if (!_expired && !_released) _heartbeat = _clock.GetTimestamp(); }

    private void CheckHeartbeat()
    {
        lock (_gate)
        {
            if (_released || _expired || _clock.GetElapsedTime(_heartbeat) < TimeSpan.FromSeconds(8)) return;
            _expired = true;
            _safety.Poison();
        }
        _timeout();
    }

    internal Task StopAsync(Action stop, Action disposeNative, Action freeSink, object resources)
    {
        lock (_gate) return _stop ??= StopCoreAsync(stop, disposeNative, freeSink, resources);
    }

    private async Task StopCoreAsync(Action stop, Action disposeNative, Action freeSink, object resources)
    {
        // Stop must not race a still-running ctor/Play. A wedged startup consumes the same bounded
        // teardown budget and keeps the resource holder rooted as its fields finish being assigned.
        var stopped = Task.Run(async () =>
        {
            try { await _startup.ConfigureAwait(false); } catch { /* partial startup still needs Stop */ }
            stop();
        });
        try
        {
            await stopped.WaitAsync(TimeSpan.FromMilliseconds(500), _clock);
            // Disposal can itself enter native code. Never execute it on the dispatcher either.
            await Task.Run(disposeNative).WaitAsync(TimeSpan.FromMilliseconds(500), _clock);
            freeSink(); // UI-owned bitmap; Stop has joined every native callback.
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Bubble count native teardown did not settle ({Error}); quarantining resources for process lifetime", ex.GetType().Name);
            _safety.Quarantine(resources);
            // Observe faults without ever disposing a quarantined graph on late completion.
            _ = stopped.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        finally
        {
            lock (_gate) _released = true;
            _watch.Dispose();
        }
    }
}
