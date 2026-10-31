using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services;

/// <summary>The head supplies capture, recognition and UI-thread dispatch. Capture and UI dispatch stay in the head.</summary>
public interface IScreenOcrRuntime
{
    void UpdateInterval(int intervalMs);
}

public static class CoreScreenOcr
{
    // Remains null until a head has an available recognition backend.
    public static IScreenOcrRuntime? Service { get; set; }
}

/// <summary>
/// Scan cadence from WPF Services/ScreenOcrService.cs:51-125. The backend owns its
/// word types and marshals dispatch to its UI thread, as WPF does at :143-199.
/// </summary>
public sealed class ScreenOcrScanLoop<T> : IScreenOcrRuntime, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task<T>> _scan;
    private readonly Func<T, CancellationToken, Task> _dispatch;
    private readonly Func<bool> _needsConfirmation;
    private readonly TimeProvider _time;
    private readonly Action<Exception> _onError;
    private ITimer? _timer;
    private CancellationTokenSource? _run;
    private bool _busy, _disposed;

    public ScreenOcrScanLoop(Func<CancellationToken, Task<T>> scan,
        Func<T, CancellationToken, Task> dispatch, Func<bool> needsConfirmation,
        TimeProvider? time = null, Action<Exception>? onError = null)
    {
        _scan = scan;
        _dispatch = dispatch;
        _needsConfirmation = needsConfirmation;
        _time = time ?? TimeProvider.System;
        _onError = onError ?? (_ => { });
    }

    // WPF :51-61: first scan waits one full interval; repeated Start keeps the timer.
    public void Start(int intervalMs)
    {
        lock (_gate)
        {
            if (_disposed || _run != null) return;
            _run = new CancellationTokenSource();
            var interval = TimeSpan.FromMilliseconds(intervalMs);
            _timer = _time.CreateTimer(Tick, null, interval, interval);
        }
    }

    public void UpdateInterval(int intervalMs)
    {
        lock (_gate)
        {
            var interval = TimeSpan.FromMilliseconds(intervalMs);
            _timer?.Change(interval, interval); // WPF :76-82: stopped is a no-op
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _run?.Cancel();
            _run?.Dispose();
            _run = null;
        }
    }

    private async void Tick(object? state) => await ScanTickAsync().ConfigureAwait(false);

    internal async Task ScanTickAsync()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || _run == null || _busy) return;
            _busy = true; // WPF :87-89: never overlap discovery/confirmation scans
            token = _run.Token;
        }
        try
        {
            await ScanAndDispatch(token).ConfigureAwait(false);
            // WPF :103-115: at most three follow-ups, 200ms apart, only while needed.
            for (int i = 0; i < 3 && !token.IsCancellationRequested && _needsConfirmation(); i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), _time, token).ConfigureAwait(false);
                await ScanAndDispatch(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { _onError(ex); }
        finally { lock (_gate) _busy = false; }
    }

    private async Task ScanAndDispatch(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var words = await _scan(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); // a stopped/disposed scan must not dispatch stale words
        await _dispatch(words, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
