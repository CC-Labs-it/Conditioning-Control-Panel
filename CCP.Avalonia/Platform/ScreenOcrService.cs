using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Ocr;
using ConditioningControlPanel.Services.ScreenCapture;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// Scan orchestration. KeywordTriggerHead.SyncOcr owns it and supplies the delivery to the consumer.
internal sealed class ScreenOcrService : IDisposable
{
    internal readonly record struct Word(string Text, Rectangle Bounds, string? Screen = null);
    private readonly Func<CancellationToken, Task<IReadOnlyList<Word>>> _scan;
    private readonly Func<bool> _hasAccess;
    private readonly Func<IReadOnlyList<Word>, CancellationToken, Task<bool>> _deliver;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private Timer? _timer;
    private CancellationTokenSource? _run;
    private int _scanning;
    internal int IntervalMs { get; private set; }

    /// <summary>The live <see cref="KeywordTriggerHead.OcrSource"/>: X11/GDI or, on Wayland and Flatpak, the
    /// Screenshot portal, through the Core engine. Null only where nothing can capture (not Windows or Linux)
    /// or the OCR models are missing; <see cref="UnavailableReasonKey"/> then says which.</summary>
    internal static Func<CancellationToken, Task<IReadOnlyList<Word>>>? CreateSource(IScreenFrameSource? frames = null, IScreenOcrEngine? engine = null)
    {
        frames ??= ScreenFrameSourceFactory.Create();
        if (frames is UnavailableScreenFrames) { _reasonKey = "ocr_unavailable_capture"; return null; }
        engine ??= new RapidScreenOcrEngine(); // one per process; its ORT sessions live until exit
        if (!engine.IsAvailable) { _reasonKey = "ocr_unavailable_models"; return null; }
        _reasonKey = null;
        return Scan(frames, engine);
    }

    private static volatile string? _reasonKey;
    /// <summary>Raised off the UI thread when a scan finds capture refused (the portal answer ends it for the session).</summary>
    internal static event Action? UnavailableChanged;

    /// <summary>Localization key for why Screen OCR reads nothing, or null when it can run. WPF has no such line:
    /// its ScreenOcrService.cs:41-43 only logs a missing OCR language pack and Start (:55) silently does nothing.</summary>
    internal static string? UnavailableReasonKey() =>
        PortalScreenFrames.Refused ? "ocr_unavailable_refused" : KeywordTriggerHead.OcrSource == null ? _reasonKey : null;

    // Every monitor's frame through the engine, mapped to desktop pixels. Recognition is CPU-bound: off the UI thread.
    // An unchanged monitor re-delivers its last words without recognition: WPF ScreenOcrService.cs:128-140 re-reads
    // every screen each scan, so the quick confirmations (:85-115) see the same words either way.
    internal static Func<CancellationToken, Task<IReadOnlyList<Word>>> Scan(IScreenFrameSource frames, IScreenOcrEngine engine)
    {
        var changes = new ScreenFrameChanges();
        var cache = new Dictionary<string, Word[]>();
        var run = CancellationToken.None; // each start of the scanner has a new token: a new run drops the cache
        return async token =>
        {
            var result = await frames.CaptureAsync(token).ConfigureAwait(false);
            if (result.Status != ScreenFrameStatus.Available)
            {
                if (PortalScreenFrames.Refused) UnavailableChanged?.Invoke();
                return [];
            }
            return await Task.Run<IReadOnlyList<Word>>(() =>
            {
                lock (cache)
                {
                    if (token != run) { changes.Reset(); cache.Clear(); run = token; }
                    return result.Frames.SelectMany(f =>
                    {
                        if (!changes.Changed(f) && cache.TryGetValue(f.Screen, out var last)) return last;
                        cache.Remove(f.Screen); // a cancelled recognition must not leave stale words behind
                        return cache[f.Screen] = engine.Recognize(f, token)
                            .Select(w => new Word(w.Text, f.ToScreen(w.Bounds), f.Screen)).ToArray();
                    }).ToArray();
                }
            }, token).ConfigureAwait(false);
        };
    }

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
