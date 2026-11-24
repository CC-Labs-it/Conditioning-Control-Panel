using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// Serializes native volume calls and prevents them from racing player disposal.
internal sealed class VlcVolumePump(Action<int> apply) : IDisposable
{
    private readonly object _gate = new();
    private int _pending = -1, _running, _closed;

    internal void Push(int value)
    {
        if (Volatile.Read(ref _closed) != 0) return;
        Volatile.Write(ref _pending, Math.Max(0, value));
        Start();
    }

    private void Start()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) == 0)
            _ = Task.Run(Drain);
    }

    private void Drain()
    {
        do
        {
            int value;
            while ((value = Interlocked.Exchange(ref _pending, -1)) >= 0)
            {
                lock (_gate)
                {
                    if (_closed != 0) break;
                    try { apply(value); }
                    catch (Exception ex) { Log.Debug("Video volume update failed: {Error}", ex.Message); }
                }
            }
            Volatile.Write(ref _running, 0);
        } while (Volatile.Read(ref _pending) >= 0 && Volatile.Read(ref _closed) == 0
                 && Interlocked.CompareExchange(ref _running, 1, 0) == 0);
    }

    public void Dispose()
    {
        lock (_gate) Volatile.Write(ref _closed, 1);
    }
}
