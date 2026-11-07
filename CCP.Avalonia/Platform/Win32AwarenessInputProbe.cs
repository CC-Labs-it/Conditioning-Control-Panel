using System;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Platform;

// Samples input events, not keys or text. Mirrors WPF Win32InputProbe.
internal sealed class Win32AwarenessInputProbe : IInputProbe
{
    private readonly Func<(bool Success, uint Tick)> _input;
    private readonly Func<(bool Success, int X, int Y)> _cursorPosition;
    private readonly Func<uint> _tickCount;
    private readonly TimeSpan _interval;
    private readonly bool[] _samples = new bool[16];
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _running, _disposed, _burst;
    private int _index, _count, _x, _y;
    private uint _lastTick;

    internal Win32AwarenessInputProbe() : this(ReadInput, ReadCursor, () => unchecked((uint)Environment.TickCount), TimeSpan.FromMilliseconds(250)) { }

    internal Win32AwarenessInputProbe(Func<(bool, uint)> input, Func<(bool, int, int)> cursorPosition, Func<uint> tickCount, TimeSpan interval)
    {
        _input = input;
        _cursorPosition = cursorPosition;
        _tickCount = tickCount;
        _interval = interval;
    }

    public int IdleSeconds
    {
        get
        {
            try
            {
                var input = _input();
                return input.Success ? (int)(unchecked(_tickCount() - input.Tick) / 1000) : 0;
            }
            catch { return 0; }
        }
    }

    public bool IsTypingBurst { get { lock (_gate) return _burst; } }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _running = true;
            if (_timer == null) _timer = new Timer(_ => Sample(), null, _interval, _interval);
            else _timer.Change(_interval, _interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Array.Clear(_samples, 0, _samples.Length);
            _index = _count = 0;
            _burst = false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _timer?.Dispose();
            _timer = null;
            _burst = false;
        }
    }

    internal void Sample()
    {
        lock (_gate)
        {
            if (!_running || _disposed) return;
            try
            {
                var input = _input();
                if (!input.Success) return;
                var cursor = _cursorPosition();
                bool moved = false;
                if (cursor.Success)
                {
                    moved = cursor.X != _x || cursor.Y != _y;
                    _x = cursor.X;
                    _y = cursor.Y;
                }
                bool fresh = input.Tick != _lastTick;
                _lastTick = input.Tick;
                Push(fresh && !moved);
            }
            catch { Push(false); }
        }
    }

    private void Push(bool typing)
    {
        if (_samples[_index]) _count--;
        _samples[_index] = typing;
        if (typing) _count++;
        _index = (_index + 1) % _samples.Length;
        _burst = _count >= 8;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size, Tick; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    private static (bool, uint) ReadInput()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return (GetLastInputInfo(ref info), info.Tick);
    }

    private static (bool, int, int) ReadCursor()
    {
        var success = GetCursorPos(out var point);
        return (success, point.X, point.Y);
    }
}
