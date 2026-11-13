using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Tests;

/// <summary>Starts one bootstrap and preserves it when a caller times out.</summary>
internal sealed class WpfBootstrapWait
{
    private readonly Lazy<Task> _startup;

    internal WpfBootstrapWait(Func<Task> start) =>
        _startup = new Lazy<Task>(start, LazyThreadSafetyMode.ExecutionAndPublication);

    internal void Wait(TimeSpan timeout)
    {
        Task startup;
        try { startup = _startup.Value; }
        catch (Exception ex) { throw new InvalidOperationException("WPF render harness: Application bootstrap failed", ex); }

        try
        {
            if (!startup.Wait(timeout))
                throw new InvalidOperationException("WPF render harness: Application bootstrap timed out");
        }
        catch (AggregateException) { /* Rethrow the original exception below. */ }

        try { startup.GetAwaiter().GetResult(); }
        catch (Exception ex) { throw new InvalidOperationException("WPF render harness: Application bootstrap failed", ex); }
    }
}
