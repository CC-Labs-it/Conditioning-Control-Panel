using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Windows.Media.Control;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/Awareness/SmtcMediaWatcher.cs:34. Metadata remains in memory only.
internal sealed class SmtcMediaWatcher : IMediaWatcher
{
    private readonly object _gate = new();
    private readonly Func<Task<object?>> _acquire;
    private readonly Func<object, Task<MediaSample?>> _read;
    private readonly MediaSessionWatcher _watcher;
    private object? _manager;
    private bool _disposed;

    internal SmtcMediaWatcher() : this(AcquireNativeAsync, ReadNativeAsync) { }

    internal SmtcMediaWatcher(Func<Task<object?>> acquire, Func<object, Task<MediaSample?>> read,
        TimeProvider? time = null)
    {
        _acquire = acquire;
        _read = read;
        _watcher = new MediaSessionWatcher(InitializeAsync, ReadAsync, time);
    }

    public MediaSample? Current => _watcher.Current;
    public bool IsAvailable => _watcher.IsAvailable;
    public void Start() => _watcher.Start();
    public void Stop() => _watcher.Stop();
    internal Task StartCompletion => _watcher.StartCompletion;
    internal Task PollAsync() => _watcher.PollAsync();

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _manager = null; }
        _watcher.Dispose();
    }

    private async Task<bool> InitializeAsync()
    {
        object? manager;
        try { manager = await _acquire().ConfigureAwait(false); }
        catch { return false; }
        lock (_gate)
        {
            if (_disposed) return false;
            _manager = manager;
            return manager != null;
        }
    }

    private Task<MediaSample?> ReadAsync()
    {
        object? manager;
        lock (_gate) manager = _manager;
        return manager == null ? Task.FromResult<MediaSample?>(null) : _read(manager);
    }

    // The delegates also let tests exercise sessions that vanish between native calls.
    internal static async Task<MediaSample?> ReadSessionAsync(Func<object?> session,
        Func<object, Task<(string? Title, string? Artist)?>> properties,
        Func<object, string?> playback, Func<object, TimeSpan?> position)
    {
        object? current;
        try { current = session(); } catch { return null; }
        if (current == null) return null;
        (string? Title, string? Artist)? props;
        try { props = await properties(current).ConfigureAwait(false); } catch { return null; }
        if (props == null) return null;
        var sample = MediaSample.FromSession(props.Value.Title, props.Value.Artist, null, TimeSpan.Zero);
        if (sample == null) return null;
        string? state = null;
        TimeSpan? elapsed = null;
        try { state = playback(current); } catch { }
        try { elapsed = position(current); } catch { }
        return sample with { PlaybackState = state ?? "Unknown", Position = elapsed ?? TimeSpan.Zero };
    }

    private static Task<object?> AcquireNativeAsync() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
        ? AcquireWindowsAsync() : Task.FromResult<object?>(null);

    [SupportedOSPlatform("windows10.0.17763")]
    private static async Task<object?> AcquireWindowsAsync() =>
        await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

    private static Task<MediaSample?> ReadNativeAsync(object manager) =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
            ? ReadWindowsAsync((GlobalSystemMediaTransportControlsSessionManager)manager)
            : Task.FromResult<MediaSample?>(null);

    [SupportedOSPlatform("windows10.0.17763")]
    private static Task<MediaSample?> ReadWindowsAsync(GlobalSystemMediaTransportControlsSessionManager manager) =>
        ReadSessionAsync(() => manager.GetCurrentSession(), async session =>
        {
            var props = await ((GlobalSystemMediaTransportControlsSession)session).TryGetMediaPropertiesAsync();
            return props == null ? null : (props.Title, props.Artist);
        }, session => ((GlobalSystemMediaTransportControlsSession)session).GetPlaybackInfo()?.PlaybackStatus.ToString(),
           session => ((GlobalSystemMediaTransportControlsSession)session).GetTimelineProperties()?.Position);
}
