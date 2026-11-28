using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The camera list scan, kept off the UI thread (7.4.2, ccp-bugs #1718: a hang report's UI stack sat
/// in the DirectShow COM enumeration called from ShowTab). One scan at a time: a second ask while one
/// runs gets the running scan's answer instead of starting another, so a wedged driver is asked once.
/// </summary>
internal sealed class WebcamDeviceScan
{
    private readonly object _gate = new();
    private Task<IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice>>? _running;

    /// <summary>
    /// Starts a scan with <paramref name="enumerate"/> on its own background thread, or joins the one
    /// that is running. <paramref name="started"/> is true only for the caller that started it, which
    /// is the one that fills the picker. A scan that throws answers an empty list.
    /// </summary>
    public Task<IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice>> Run(
        Func<IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice>> enumerate, out bool started)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false })
            {
                started = false;
                return _running;
            }
            started = true;
            return _running = OffThread(enumerate);
        }
    }

    /// <summary>True while a scan is running.</summary>
    public bool Busy
    {
        get { lock (_gate) return _running is { IsCompleted: false }; }
    }

    /// <summary>
    /// A dedicated background STA thread, the apartment the picker used to enumerate in on the UI
    /// thread, so the COM objects see the same threading they always did.
    /// </summary>
    private static Task<IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice>> OffThread(
        Func<IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice>> enumerate)
    {
        var done = new TaskCompletionSource<IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.TrySetResult(enumerate()); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamDeviceScan: camera scan threw");
                done.TrySetResult(Array.Empty<WebcamDeviceEnumerator.WebcamDevice>());
            }
        })
        {
            IsBackground = true,
            Name = "CCP camera scan",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
