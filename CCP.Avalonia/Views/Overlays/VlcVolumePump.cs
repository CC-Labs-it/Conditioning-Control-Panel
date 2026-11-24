using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// The gate protects bookkeeping only. Native calls never hold it.
internal sealed class VlcVolumePump(Action<int> apply)
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pending = -1;
    private bool _running, _closing;

    internal void Push(int value)
    {
        lock (_gate)
        {
            if (_closing) return;
            _pending = Math.Max(0, value);
            if (_running) return;
            _running = true;
            _ = Task.Run(Drain);
        }
    }

    private void Drain()
    {
        while (true)
        {
            int value;
            lock (_gate)
            {
                if (_closing || _pending < 0)
                {
                    _running = false;
                    if (_closing) _closed.TrySetResult();
                    return;
                }
                value = _pending;
                _pending = -1;
            }
            try { apply(value); }
            catch (Exception ex) { Log.Debug("Video volume update failed: {Error}", ex.Message); }
        }
    }

    // Reject new updates immediately; the task completes after any native call returns.
    internal Task CloseAsync()
    {
        lock (_gate)
        {
            _closing = true;
            _pending = -1;
            if (!_running) _closed.TrySetResult();
            return _closed.Task;
        }
    }
}

// Stop can run alongside a stalled volume call. Disposal and frame cleanup require both to finish.
internal static class VlcPlaybackRetirement
{
    private static readonly ConcurrentBag<object> Quarantine = new();

    internal static Task Retire(VlcVolumePump? pump, Action stop, Action dispose, Action cleanup,
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
