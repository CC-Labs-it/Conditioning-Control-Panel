using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>Second-launch handoffs (SingleInstance) that arrive before the shell has opened. They are kept
    /// in arrival order and routed once it opens; afterwards each one is delivered directly. WPF only consumes a
    /// handoff once its MainWindow exists (App.xaml.cs:2226-2229), so a request during a cold start waited for
    /// the next relaunch there.</summary>
    internal sealed class HandoffQueue
    {
        readonly Queue<string?> _pending = new();
        Func<string?, Task>? _deliver;

        /// <summary>Completes at once while queued, so the second launch is acked and exits.</summary>
        internal Task Enqueue(string? payload)
        {
            lock (_pending)
            {
                if (_deliver != null) return _deliver(payload);
                _pending.Enqueue(payload);
                return Task.CompletedTask;
            }
        }

        internal void Ready(Func<string?, Task> deliver)
        {
            lock (_pending)
            {
                _deliver = deliver;
                while (_pending.TryDequeue(out var payload))
                    _ = deliver(payload).ContinueWith(t => Serilog.Log.Warning(t.Exception, "Queued handoff failed"),
                        TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}
