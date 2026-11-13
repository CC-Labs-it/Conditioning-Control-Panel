using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF VideoService.cs:7071, 7114, 7292. This slice only implements the escape hatch.
internal sealed class VideoWedgeWatchdog : IDisposable
{
    private readonly DispatcherTimer _heartbeat;
    private readonly Timer _poll;
    private readonly object _gate = new();
    private readonly Dictionary<IntPtr, OverlayBackend> _handles = new();
    private readonly Action<IntPtr, OverlayBackend> _hide;
    private long _lastBeat = Stopwatch.GetTimestamp();
    private int _stallSeen, _escaped, _disposed;

    internal VideoWedgeWatchdog(Action<IntPtr, OverlayBackend>? hide = null)
    {
        _hide = hide ?? Hide;
        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _heartbeat.Tick += (_, _) => Interlocked.Exchange(ref _lastBeat, Stopwatch.GetTimestamp());
        _heartbeat.Start();
        _poll = new Timer(_ => Poll(), null, 1000, 1000);
    }

    internal bool StallSeen => Volatile.Read(ref _stallSeen) != 0;
    internal void Add(IntPtr handle, OverlayBackend backend)
    {
        if (handle == IntPtr.Zero || backend == OverlayBackend.None) return;
        lock (_gate) _handles[handle] = backend;
    }
    internal void Remove(IntPtr handle) { lock (_gate) _handles.Remove(handle); }

    private void Poll()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var ms = (long)Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastBeat)).TotalMilliseconds;
        if (ms < VideoWedgeLadder.StallMs) return;
        Volatile.Write(ref _stallSeen, 1);
        if (VideoWedgeLadder.NextRung(ms, 0, rescueAllowed: false, retireRung: true)
            != VideoWedgeLadder.WedgeAction.EscapeHatch || Interlocked.Exchange(ref _escaped, 1) != 0) return;
        KeyValuePair<IntPtr, OverlayBackend>[] handles;
        lock (_gate) { handles = new KeyValuePair<IntPtr, OverlayBackend>[_handles.Count]; ((ICollection<KeyValuePair<IntPtr, OverlayBackend>>)_handles).CopyTo(handles, 0); }
        ReleaseHandles(handles);
    }

    // One blocked native call must not prevent the other windows from being released.
    internal void ReleaseHandles(KeyValuePair<IntPtr, OverlayBackend>[] handles)
    {
        foreach (var (handle, backend) in handles)
            _ = Task.Run(() =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                try { _hide(handle, backend); }
                catch (Exception ex) { Log.Debug(ex, "Video wedge window release failed"); }
            });
    }

    private static void Hide(IntPtr handle, OverlayBackend backend)
    {
        if (backend == OverlayBackend.Win32) Win32Overlay.ReleaseWedgedWindow(handle);
        else if (backend == OverlayBackend.X11) X11Overlay.ReleaseWedgedWindow(handle);
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        _poll.Dispose();
        _heartbeat.Stop();
    }
}
