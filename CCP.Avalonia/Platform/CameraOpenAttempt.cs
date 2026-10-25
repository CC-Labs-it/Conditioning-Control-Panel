using System;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF's camera-open watchdog. The worker owns the source until publication;
/// timeout/panic transfers cleanup back to that worker, never racing Dispose against Open.</summary>
internal sealed class CameraOpenAttempt
{
    private Task? _worker;
    internal bool TimedOut { get; private set; }
    internal bool IsPending => _worker is { IsCompleted: false };

    internal IFrameSource? Open(Func<IFrameSource> factory, Func<bool> stopped, TimeSpan timeout)
    {
        var gate = new object();
        bool abandoned = false;
        IFrameSource? published = null;
        _worker = Task.Run(() =>
        {
            IFrameSource? source = null;
            try
            {
                source = factory();
                if (source is OpenCvFrameSource cv) cv.Abandoned = () => { lock (gate) return abandoned || stopped(); };
                if (!source.Open()) return;
                lock (gate)
                {
                    if (abandoned || stopped()) return;
                    published = source;
                    source = null;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[Webcam] camera open failed"); }
            finally { try { source?.Dispose(); } catch (Exception ex) { Log.Warning(ex, "[Webcam] camera cleanup failed"); } }
        });
        bool completed = _worker.Wait(timeout);
        TimedOut = !completed;
        lock (gate)
        {
            abandoned = !completed || stopped();
            if (!abandoned) return published;
        }
        // A worker that published just before timeout has already relinquished ownership.
        published?.Dispose();
        return null;
    }
}
