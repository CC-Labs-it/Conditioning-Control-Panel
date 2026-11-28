using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>Runs one camera enumeration at a time away from the UI thread.</summary>
internal sealed class WebcamScanCoordinator<T>
{
    private readonly object _gate = new();
    private Task<IReadOnlyList<T>>? _running;

    public Task<IReadOnlyList<T>> Run(Func<IReadOnlyList<T>> enumerate, out bool started)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false })
            {
                started = false;
                return _running;
            }
            started = true;
            var done = new TaskCompletionSource<IReadOnlyList<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _running = done.Task;
            var thread = new Thread(() =>
            {
                try { done.TrySetResult(enumerate()); }
                catch (Exception ex)
                {
                    Log.Warning(ex, "WebcamDeviceScan: camera scan threw");
                    done.TrySetResult(Array.Empty<T>());
                }
            }) { IsBackground = true, Name = "CCP camera scan" };
            if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return _running;
        }
    }
}
