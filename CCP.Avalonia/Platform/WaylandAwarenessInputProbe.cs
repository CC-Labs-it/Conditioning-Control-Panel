using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF AwarenessProbes.cs:165-190 reads machine-wide input idle. Under Wayland XScreenSaver only sees
// XWayland input, so idle comes from the compositor. The typing burst stays on XWayland: the only
// foreground the observer can read there is an X11 window (X11ForegroundProbe), whose input XWayland sees.
internal sealed class WaylandAwarenessInputProbe : IInputProbe
{
    private readonly IInputProbe _burst;
    private readonly Func<Task<int?>> _read;
    private readonly object _gate = new();
    private int _last = -1, _generation; // _last < 0: idle age is unknown
    private bool _reading;
    private Task? _pending;
    private WaylandIdleNotify? _notify; // exists only while awareness runs, as WPF AwarenessProbes.cs:241 stops its sampler
    internal WaylandIdleNotify? Notifier { get { lock (_gate) return _notify; } }
    internal Task? PendingRead { get { lock (_gate) return _pending; } }

    internal WaylandAwarenessInputProbe(IInputProbe burst, Func<Task<int?>>? read = null)
    {
        _burst = burst;
        _read = read ?? ReadCompositor;
    }

    internal static bool IsSession() => OperatingSystem.IsLinux()
        && (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));

    // Polled every 1.5 s off a timer, so it returns the last finished read and starts the next one.
    // Fail closed: without an idle source the observer treats the user as away and records nothing.
    public int IdleSeconds
    {
        get
        {
            lock (_gate)
            {
                if (!_reading)
                {
                    _reading = true;
                    int generation = _generation;
                    _pending = Task.Run(_read).ContinueWith(t =>
                    {
                        lock (_gate)
                        {
                            if (generation != _generation) return;
                            _last = t.IsCompletedSuccessfully && t.Result is int s ? Math.Max(0, s) : -1;
                            _reading = false;
                        }
                    }, TaskScheduler.Default);
                }
                return _last < 0 ? AwarenessObserver.AfkSuspendSeconds : _last;
            }
        }
    }

    public bool IsTypingBurst => _burst.IsTypingBurst;
    private void ResetIdle(bool run)
    {
        lock (_gate)
        {
            _generation++; _last = -1; _reading = false;
            _notify?.Stop();
            // Idle reads exact from this threshold and 0 below it; 30 s is the lowest gate the observer applies.
            _notify = run ? new(AwarenessObserverPolicy.FullscreenRecentInputSeconds * 1000, observeInitialActivity: true) : null;
        }
    }
    public void Start() { ResetIdle(true); _burst.Start(); }
    public void Stop() { ResetIdle(false); _burst.Stop(); }
    public void Dispose() { ResetIdle(false); _burst.Dispose(); }

    // ext-idle-notify (KDE, wlroots), else Mutter (GNOME). A new notification cannot report
    // pre-existing idle age; a short-lived witness confirms real input without waiting 30 seconds.
    private Task<int?> ReadCompositor()
    {
        var notify = Notifier;
        if (X11ActiveWindow.Disabled || notify is null) return Task.FromResult<int?>(null);
        notify.Start();
        return ReadCompositor(notify, static () => DesktopIdle.ReadWaylandAsync(DesktopIdle.ReadMutterMillisecondsAsync));
    }

    internal static Task<int?> ReadCompositor(WaylandIdleNotify notify, Func<Task<int?>> fallback)
    {
        if (notify.Live) return Task.FromResult(ConfirmedIdle(notify));
        if (!notify.Done) return Task.FromResult<int?>(null);
        return fallback();
    }

    internal static int? ConfirmedIdle(WaylandIdleNotify notify) =>
        notify.ActivityObserved ? notify.Seconds : null;
}
