using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Awareness;

/// <summary>The WPF SMTC watcher's portable lifecycle. Platform heads supply only acquisition
/// and a single session read. Metadata stays in memory; this class never logs or persists it.</summary>
public sealed class MediaSessionWatcher : IMediaWatcher
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly Func<Task<bool>> _initialize;
    private readonly Func<Task<MediaSample?>> _read;
    private readonly TimeProvider _time;
    private Task<bool>? _initialization;
    private ITimer? _timer;
    private MediaSample? _current;
    private bool _available, _started, _disposed, _reading;
    private long _generation;
    private string? _mediaTitle;
    private TimeSpan _mediaPosition;
    private int _mediaRepeats;
    private bool _mediaChangePending;

    public MediaSessionWatcher(Func<Task<bool>> initialize, Func<Task<MediaSample?>> read,
        TimeProvider? time = null)
    {
        _initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _time = time ?? TimeProvider.System;
    }

    public MediaSample? Current { get { lock (_gate) return _current; } }
    public bool IsAvailable { get { lock (_gate) return _available; } }

    /// <summary>Fold the current sample on the observer tick, not on the backend poll.
    /// WPF AwarenessObserver.cs:944-970 and :835-838. Titles remain local to MediaInfo.</summary>
    public MediaInfo? TrackMedia()
    {
        lock (_gate)
        {
            var sample = _current;
            if (sample == null)
            {
                _mediaTitle = null;
                _mediaPosition = TimeSpan.Zero;
                _mediaRepeats = 0;
                return null;
            }
            if (!string.Equals(sample.Title, _mediaTitle, StringComparison.Ordinal))
            {
                _mediaRepeats = 1;
                if (_mediaTitle != null) _mediaChangePending = true;
                _mediaTitle = sample.Title;
            }
            else if (sample.Position + TimeSpan.FromSeconds(10) < _mediaPosition)
            {
                _mediaRepeats++;
                if (_mediaRepeats >= ActivityLedger.MediaLoopMinimum) _mediaChangePending = true;
            }
            _mediaPosition = sample.Position;
            return new MediaInfo(sample.Title, sample.Artist, sample.PlaybackState, _mediaRepeats);
        }
    }

    /// <summary>Consume only when the observer reaches the stationary-app media candidate.
    /// Earlier candidates leave the pending beat intact (WPF AwarenessObserver.cs:743-747).</summary>
    public bool TakeMediaChange()
    {
        lock (_gate)
        {
            bool pending = _mediaChangePending;
            _mediaChangePending = false;
            return pending;
        }
    }

    /// <summary>The media half of stop/wipe erasure (WPF AwarenessObserver.cs:420-423).
    /// Does not acquire a session or read the desktop.</summary>
    public void ResetMediaState()
    {
        lock (_gate)
        {
            _mediaTitle = null;
            _mediaPosition = TimeSpan.Zero;
            _mediaRepeats = 0;
            _mediaChangePending = false;
        }
    }

    // Tests await the actual acquisition/arming operation rather than racing a background task.
    internal Task StartCompletion { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        Task<bool> initialization;
        long generation;
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
            generation = ++_generation;
            // Stop/Start while acquisition is pending must not request two managers.
            initialization = _initialization ??= Task.Run(InitializeAsync);
        }
        StartCompletion = ArmAsync(initialization, generation);
    }

    private async Task<bool> InitializeAsync()
    {
        try { return await _initialize().ConfigureAwait(false); }
        catch { return false; } // permission denied/unsupported: the optional signal is off
    }

    private async Task ArmAsync(Task<bool> initialization, long generation)
    {
        bool available = await initialization.ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || !_started || generation != _generation) return;
            _available = available;
            if (!available) { _initialization = null; return; } // retry only on a later Stop/Start
            try
            {
                bool armed = false;
                if (_timer != null)
                {
                    try { armed = _timer.Change(TimeSpan.Zero, PollInterval); }
                    catch (ObjectDisposedException) { _timer = null; }
                }
                if (!armed)
                    _timer = _time.CreateTimer(state => { _ = PollAsync(); }, null, TimeSpan.Zero, PollInterval);
            }
            catch { _available = false; }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _started = false;
            ++_generation;
            _current = null;
            ResetMediaState();
            try { _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
            catch { try { _timer?.Dispose(); } catch { } _timer = null; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
            ++_generation;
            _available = false;
            _current = null;
            ResetMediaState();
            try { _timer?.Dispose(); } catch { }
            _timer = null;
        }
    }

    internal async Task PollAsync()
    {
        long generation;
        lock (_gate)
        {
            if (_disposed || !_started || !_available || _reading) return;
            _reading = true;
            generation = _generation;
        }
        try
        {
            var sample = await _read().ConfigureAwait(false);
            lock (_gate)
            {
                // A read may finish after pause, dispose, or a complete Stop/Start cycle.
                if (!_disposed && _started && generation == _generation) _current = sample;
            }
        }
        catch { } // torn-down session: leave the last successful read, as WPF does
        finally { lock (_gate) _reading = false; }
    }
}
