using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// Scan orchestration only. A platform capture/recognition source and the OCR keyword consumer
// are still needed before this can be attached to the Awareness switches.
internal sealed class ScreenOcrService : IDisposable
{
    internal readonly record struct Word(string Text, Rectangle Bounds);
    private readonly Func<CancellationToken, Task<IReadOnlyList<Word>>> _scan;
    private readonly Func<bool> _hasAccess;
    private readonly Func<IReadOnlyList<Rectangle>> _ownWindows;
    private readonly Func<IReadOnlyList<Word>, bool> _deliver;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private Timer? _timer;
    private CancellationTokenSource? _run;
    private int _scanning;
    internal int IntervalMs { get; private set; }

    internal ScreenOcrService(Func<CancellationToken, Task<IReadOnlyList<Word>>> scan,
        Func<bool> hasAccess, Func<IReadOnlyList<Rectangle>> ownWindows,
        Func<IReadOnlyList<Word>, bool> deliver, Func<int, CancellationToken, Task>? delay = null)
    {
        _scan = scan; _hasAccess = hasAccess; _ownWindows = ownWindows;
        _deliver = deliver; _delay = delay ?? Task.Delay;
    }

    // WPF MainWindow.Awareness.cs:414-428,461-468. Access is also checked at each delivery.
    internal void Sync(bool master, bool ocr, int intervalMs)
    {
        lock (_gate)
        {
            if (!master || !ocr || !_hasAccess()) { StopLocked(); return; }
            IntervalMs = Math.Max(1, intervalMs);
            if (_run == null)
            {
                _run = new CancellationTokenSource();
                _timer = new Timer(_ => _ = TickAsync(), null, IntervalMs, IntervalMs);
            }
            else _timer?.Change(IntervalMs, IntervalMs);
        }
    }

    internal void Stop() { lock (_gate) StopLocked(); }
    private void StopLocked()
    {
        _timer?.Dispose(); _timer = null;
        _run?.Cancel(); _run?.Dispose(); _run = null;
    }

    // One discovery and at most three quick confirmations (WPF ScreenOcrService.cs:85-115).
    internal async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _scanning, 1) != 0) return;
        try
        {
            CancellationToken token;
            lock (_gate)
            {
                if (_run == null) return;
                if (!_hasAccess()) { StopLocked(); return; }
                token = _run.Token;
            }
            for (var quick = 0; quick <= 3; quick++)
            {
                if (quick > 0) await _delay(200, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (token.IsCancellationRequested) return;
                    if (!_hasAccess()) { StopLocked(); return; }
                }
                var words = await _scan(token).ConfigureAwait(false);
                bool needsConfirmation;
                lock (_gate)
                {
                    if (token.IsCancellationRequested) return;
                    if (!_hasAccess()) { StopLocked(); return; }
                    var rects = _ownWindows();
                    var filtered = words.Where(w => !rects.Any(r => r.IntersectsWith(w.Bounds))).ToArray();
                    needsConfirmation = _deliver(filtered);
                }
                if (!needsConfirmation) return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Log.Debug("Screen OCR scan failed"); }
        finally { Volatile.Write(ref _scanning, 0); }
    }

    public void Dispose() => Stop();
}
