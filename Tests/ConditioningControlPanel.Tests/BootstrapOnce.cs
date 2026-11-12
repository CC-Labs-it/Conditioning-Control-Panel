using System;
using System.Threading;

namespace ConditioningControlPanel.Tests;

// A timed-out caller must leave the original bootstrap available to later callers.
internal sealed class BootstrapOnce
{
    private readonly object _gate = new();
    private ManualResetEventSlim? _completed;
    private Exception? _failure;

    internal void Ensure(Action<Action<Exception?>> start, TimeSpan timeout)
    {
        lock (_gate)
        {
            if (_completed == null)
            {
                _completed = new ManualResetEventSlim(false);
                start(failure => { _failure = failure; _completed.Set(); });
            }
            if (!_completed.Wait(timeout))
                throw new InvalidOperationException("WPF render harness: Application bootstrap timed out");
            if (_failure != null)
                throw new InvalidOperationException("WPF render harness: Application bootstrap failed", _failure);
        }
    }
}
