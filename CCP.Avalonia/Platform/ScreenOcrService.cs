using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// Scan orchestration. KeywordTriggerHead.SyncOcr owns it and supplies the delivery to the consumer.
internal sealed class ScreenOcrService : IDisposable
{
    internal readonly record struct Word(string Text, Rectangle Bounds);
    private readonly Func<CancellationToken, Task<IReadOnlyList<Word>>> _scan;
    private readonly Func<bool> _hasAccess;
    private readonly Func<IReadOnlyList<Word>, CancellationToken, Task<bool>> _deliver;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private Timer? _timer;
    private CancellationTokenSource? _run;
    private int _scanning;
    internal int IntervalMs { get; private set; }

    internal ScreenOcrService(Func<CancellationToken, Task<IReadOnlyList<Word>>> scan,
        Func<bool> hasAccess, Func<IReadOnlyList<Word>, CancellationToken, Task<bool>> deliver,
        Func<int, CancellationToken, Task>? delay = null)
    {
        _scan = scan; _hasAccess = hasAccess; _deliver = deliver; _delay = delay ?? Task.Delay;
    }

    // WPF MainWindow.Awareness.cs:414-428,461-468, KeywordTriggers.cs:119. Access is checked per scan:
    // a lapse skips scans and a regain resumes them (WPF Patreon.cs:190,457), as keyboard input does here.
    internal void Sync(bool master, bool ocr, int intervalMs)
    {
        lock (_gate)
        {
            if (!master || !ocr) { StopLocked(); return; }
            var interval = Math.Max(1, intervalMs);
            if (_run == null)
            {
                IntervalMs = interval;
                _run = new CancellationTokenSource();
                _timer = new Timer(_ => _ = TickAsync(), null, IntervalMs, IntervalMs);
            }
            // A running scanner is left alone (WPF Start no-ops while running); only a new interval re-times it.
            else if (interval != IntervalMs) { IntervalMs = interval; _timer?.Change(IntervalMs, IntervalMs); }
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
                if (_run == null || !_hasAccess()) return;
                token = _run.Token;
            }
            for (var quick = 0; quick <= 3; quick++)
            {
                if (quick > 0) await _delay(200, token).ConfigureAwait(false);
                if (token.IsCancellationRequested || !_hasAccess()) return;
                var words = await _scan(token).ConfigureAwait(false);
                // In-flight results never outlive a stop or an access lapse (WPF KeywordTriggerService.cs:719).
                if (token.IsCancellationRequested || !_hasAccess()) return;
                if (!await _deliver(words, token).ConfigureAwait(false)) return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug(ex, "Screen OCR scan failed"); }
        finally { Volatile.Write(ref _scanning, 0); }
    }

    public void Dispose() => Stop();
}
