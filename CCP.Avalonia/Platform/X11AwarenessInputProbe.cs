using System;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF AwarenessProbes.cs:183-190,212-305: input age and a four-second window of
// fresh input with a stationary cursor. XScreenSaver sees X11 input, not native Wayland input.
internal sealed class X11AwarenessInputProbe : IInputProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenSaverInfo
    {
        public IntPtr Window;
        public int State, Kind;
        public UIntPtr TilOrSince, Idle, EventMask;
    }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
    [DllImport("libX11.so.6")] private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root,
        out IntPtr child, out int x, out int y, out int winX, out int winY, out uint mask);
    [DllImport("libXss.so.1")] private static extern bool XScreenSaverQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport("libXss.so.1")] private static extern IntPtr XScreenSaverAllocInfo();
    [DllImport("libXss.so.1")] private static extern int XScreenSaverQueryInfo(IntPtr display, IntPtr drawable, IntPtr info);

    private readonly object _gate = new();
    private readonly Func<(long IdleMs, int X, int Y)?> _read;
    private readonly Func<long> _clock;
    private readonly bool[] _samples = new bool[16];
    private Timer? _timer;
    private IntPtr _display, _root;
    private int _cursor, _typing, _x, _y;
    private long _lastInputAt;
    private bool _running, _disposed, _burst;

    internal X11AwarenessInputProbe(Func<(long IdleMs, int X, int Y)?>? read = null, Func<long>? clock = null)
    {
        _read = read ?? ReadNative;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public int IdleSeconds
    {
        get
        {
            lock (_gate)
            {
                if (_disposed) return 0;
                try { return (int)Math.Min(int.MaxValue, Math.Max(0, _read()?.IdleMs ?? 0) / 1000); }
                catch { return 0; }
            }
        }
    }
    public bool IsTypingBurst { get { lock (_gate) return _burst; } }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _running) return;
            _running = true;
            _timer ??= new Timer(_ => SampleOnce(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(250, 250);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Array.Clear(_samples);
            _cursor = _typing = 0;
            _burst = false;
            // Keep the last input timestamp and cursor, as WPF does across Stop/Start.
        }
    }

    internal void SampleOnce()
    {
        lock (_gate)
        {
            if (!_running || _disposed) return;
            bool typing = false;
            try
            {
                long before = _clock();
                var sample = _read();
                long after = _clock();
                if (sample is { } s)
                {
                    long idle = Math.Max(0, s.IdleMs);
                    // The server sampled somewhere inside the query. Only a non-overlapping
                    // input-time interval proves fresh input; allow 5 ms for clock skew.
                    bool fresh = before - idle > _lastInputAt + 5;
                    bool moved = s.X != _x || s.Y != _y;
                    _lastInputAt = after - idle; // latest possible time of this input
                    _x = s.X; _y = s.Y;
                    typing = fresh && !moved;
                }
            }
            catch { } // WPF treats a failed sample as not typing
            if (_samples[_cursor]) _typing--;
            _samples[_cursor] = typing;
            if (typing) _typing++;
            _cursor = (_cursor + 1) % _samples.Length;
            _burst = _typing >= 8; // 2 samples/second over four seconds
        }
    }

    private (long IdleMs, int X, int Y)? ReadNative()
    {
        if (X11ActiveWindow.Disabled || !OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return null;
        if (_display == IntPtr.Zero)
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero) return null;
            _root = XDefaultRootWindow(_display);
        }
        if (!XScreenSaverQueryExtension(_display, out _, out _)) return null;
        var info = XScreenSaverAllocInfo();
        if (info == IntPtr.Zero) return null;
        try
        {
            if (XScreenSaverQueryInfo(_display, _root, info) == 0) return null;
            int x = _x, y = _y;
            if (XQueryPointer(_display, _root, out _, out _, out int px, out int py, out _, out _, out _))
            { x = px; y = py; }
            ulong idle = Marshal.PtrToStructure<ScreenSaverInfo>(info).Idle.ToUInt64();
            return ((long)Math.Min(idle, (ulong)long.MaxValue), x, y);
        }
        finally { XFree(info); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            if (_display != IntPtr.Zero) { XCloseDisplay(_display); _display = IntPtr.Zero; }
        }
    }
}
