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
    // Idle reads exact from this threshold and 0 below it; 30 s is the lowest gate the observer applies.
    private static readonly WaylandIdleNotify Notify = new(AwarenessObserverPolicy.FullscreenRecentInputSeconds * 1000);
    private readonly IInputProbe _burst;
    private readonly Func<Task<int?>> _read;
    private int _last, _reading; // _last < 0: no compositor idle source

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
            if (Interlocked.Exchange(ref _reading, 1) == 0)
                Task.Run(_read).ContinueWith(t =>
                {
                    Volatile.Write(ref _last, t.IsCompletedSuccessfully && t.Result is int s ? Math.Max(0, s) : -1);
                    Volatile.Write(ref _reading, 0);
                }, TaskScheduler.Default);
            int last = Volatile.Read(ref _last);
            return last < 0 ? AwarenessObserver.AfkSuspendSeconds : last;
        }
    }

    public bool IsTypingBurst => _burst.IsTypingBurst;
    public void Start() => _burst.Start();
    public void Stop() => _burst.Stop();
    public void Dispose() => _burst.Dispose();

    // ext-idle-notify (KDE, wlroots), else Mutter (GNOME). Connecting reads as active so start-up
    // never fakes an away stretch; tests never read the real desktop.
    private static Task<int?> ReadCompositor()
    {
        if (X11ActiveWindow.Disabled) return Task.FromResult<int?>(null);
        Notify.Start();
        if (Notify.Live) return Task.FromResult<int?>(Notify.Seconds);
        if (!Notify.Done) return Task.FromResult<int?>(0);
        return DesktopIdle.ReadWaylandAsync(DesktopIdle.ReadMutterMillisecondsAsync);
    }
}
