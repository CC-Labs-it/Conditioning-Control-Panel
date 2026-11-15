using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

/// <summary>WPF's optional Pause key (MainWindow.xaml.cs:975-989) for native Wayland windows: a
/// GlobalShortcuts grab held only while a mandatory video plays (the grab consumes the key desktop-wide,
/// decision C), never while the panic key shadows it, during Lockdown (WPF returns first, :886) or key capture.</summary>
public partial class MainShellWindow
{
    private IDisposable? _pauseKeyRegistration;
    private DispatcherTimer? _pauseKeyWatch;
    private (bool Wanted, string Key)? _pauseKeyBinding;
    private CancellationTokenSource? _pauseKeyPending;
    private int _pauseKeyGeneration;
    internal TimeSpan PauseKeyBindTimeout = MandatoryVideoScheduler.PreRoll + TimeSpan.FromMilliseconds(100);
    internal Func<TimeSpan, Task> PauseKeyDelay = Task.Delay;
    private int _pauseDeadlineGeneration = -1;
    private Task? _pauseDeadline;
    private Task? _pauseKeyBind;
    private MandatoryVideoScheduler? _pauseKeyScheduler;

    internal bool PauseKeyWayland = OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    internal Func<MandatoryVideoScheduler> PauseKeyScheduler = () => MandatoryVideoOverlay.Instance.Scheduler;
    internal Func<bool> PauseKeyVideoPlaying = () => MandatoryVideoOverlay.Instance.Scheduler.IsPlaying;
    internal Func<bool> PauseKeyGracePause = () => MandatoryVideoOverlay.Instance.TryGracePause(fromPanicKey: false);
    internal Func<string, Action, Action, CancellationToken, Task<IDisposable?>> RegisterWaylandPauseKey = PortalPauseShortcut.BindAsync;
    internal bool PauseKeyArmed => _pauseKeyRegistration != null;

    /// <summary>Desktop startup only; render/tests do not arm keys.</summary>
    internal void StartPauseKeyPortal()
    {
        if (!PauseKeyWayland || _pauseKeyWatch != null) return;
        _pauseKeyScheduler = PauseKeyScheduler();
        _pauseKeyScheduler.PreparePlayback = PreparePauseKeyPortalAsync;
        _pauseKeyScheduler.VideoEnded += PauseVideoEnded;
        ApplyPauseKeyPortal();
        // Observe settings replacement and Lockdown; video start and end use the scheduler hooks.
        _pauseKeyWatch = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => ApplyPauseKeyPortal());
        _pauseKeyWatch.Start();
        Closed += (_, _) => StopPauseKeyPortal();
    }

    internal void StopPauseKeyPortal()
    {
        _pauseKeyWatch?.Stop();
        _pauseKeyWatch = null;
        if (_pauseKeyScheduler != null)
        {
            _pauseKeyScheduler.VideoEnded -= PauseVideoEnded;
            if (_pauseKeyScheduler.PreparePlayback == PreparePauseKeyPortalAsync) _pauseKeyScheduler.PreparePlayback = null;
            _pauseKeyScheduler = null;
        }
        _pauseKeyBinding = null;
        _pauseDeadline = null;
        _pauseDeadlineGeneration = -1;
        CancelPauseKeyRegistration();
    }

    private void PauseVideoEnded() => ApplyPauseKeyPortal();

    // Start at clip acceptance and spend the existing pre-roll on the portal. A slow or
    // unavailable portal may delay the clip by at most 100 ms beyond the normal pre-roll;
    // after that the clip starts and the request (maybe a permission dialog) keeps running
    // until it answers or the video ends.
    private async Task PreparePauseKeyPortalAsync()
    {
        var scheduler = _pauseKeyScheduler;
        if (scheduler == null) return;
        var video = scheduler.PlaybackGeneration;
        if (_pauseDeadlineGeneration != video)
        {
            _pauseDeadlineGeneration = video;
            _pauseDeadline = PauseKeyDelay(PauseKeyBindTimeout);
        }
        var timeout = _pauseDeadline!;
        while (ReferenceEquals(scheduler, _pauseKeyScheduler) && scheduler.PlaybackGeneration == video && PauseKeyVideoPlaying())
        {
            ApplyPauseKeyPortal();
            var pending = _pauseKeyBind;
            if (pending == null || pending.IsCompleted) return;
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, _pauseKeyPending!.Token);
            var completed = await Task.WhenAny(pending, timeout, cancelled);
            // A previous video's waiter must never follow or cancel a replacement video's bind.
            if (!ReferenceEquals(scheduler, _pauseKeyScheduler) || scheduler.PlaybackGeneration != video || !PauseKeyVideoPlaying()) return;
            if (completed == timeout)
            {
                if (_pauseKeyBind is { IsCompleted: false })
                    Log.Information("Pause key: portal still answering, video starts while binding continues");
                return;
            }
            // Cancellation can fire inside ApplyPauseKeyPortal, before it starts the replacement.
            if (completed == cancelled) await Task.Yield();
        }
    }

    /// <summary>WPF OnGlobalKeyPressed: the panic key wins a shared binding (by keysym, as the portal
    /// sees it), Lockdown and either key capture refuse the press.</summary>
    internal static bool PauseKeyRefused(AppSettings s) =>
        string.IsNullOrWhiteSpace(s.PauseKey)
        || PanicPolicy.PauseKeyIsShadowedByPanicKey(s.PanicKey, s.PanicKeyEnabled, s.PauseKey)
        || (s.PanicKeyEnabled && !string.IsNullOrWhiteSpace(s.PanicKey) && string.Equals(X11PanicKey.XKeyName(s.PanicKey), X11PanicKey.XKeyName(s.PauseKey), StringComparison.OrdinalIgnoreCase))
        || LockdownActive || CapturingPanicKey || Controls.AppSettings.DevicesSettingsSection.CapturingPauseKey;

    internal void ApplyPauseKeyPortal()
    {
        var s = CoreSettings.Current;
        var binding = (PauseKeyWayland && PauseKeyVideoPlaying() && !PauseKeyRefused(s), s.PauseKey?.Trim() ?? "");
        if (_pauseKeyBinding == binding) return;
        _pauseKeyBinding = binding;
        CancelPauseKeyRegistration();
        if (!binding.Item1) return;
        _pauseKeyPending = new CancellationTokenSource();
        _pauseKeyBind = BindWaylandPauseKeyAsync(binding.Item2, _pauseKeyGeneration, _pauseKeyPending.Token);
    }

    private void CancelPauseKeyRegistration()
    {
        _pauseKeyGeneration++;
        _pauseKeyBind = null;
        _pauseKeyPending?.Cancel();
        _pauseKeyPending?.Dispose();
        _pauseKeyPending = null;
        _pauseKeyRegistration?.Dispose();
        _pauseKeyRegistration = null;
    }

    private async Task BindWaylandPauseKeyAsync(string key, int generation, CancellationToken cancellation)
    {
        IDisposable? registration = null;
        try
        {
            registration = await RegisterWaylandPauseKey(key,
                () => Dispatcher.UIThread.Post(() => { if (generation == _pauseKeyGeneration) OnPortalPauseKeyPressed(key); }),
                () => Dispatcher.UIThread.Post(() => { if (generation == _pauseKeyGeneration) CancelPauseKeyRegistration(); }),
                cancellation);
            if (generation != _pauseKeyGeneration || cancellation.IsCancellationRequested) registration?.Dispose();
            else _pauseKeyRegistration = registration;
        }
        catch (Exception ex) { registration?.Dispose(); Log.Warning(ex, "Pause key Wayland registration failed"); }
    }

    /// <summary>UI thread. Rechecked here: settings or Lockdown may have changed since the last poll.</summary>
    internal void OnPortalPauseKeyPressed(string key)
    {
        if (!PauseKeyVideoPlaying() || !PanicPolicy.IsPauseKeyPress(CoreSettings.Current.PauseKey, key) || PauseKeyRefused(CoreSettings.Current)) { ApplyPauseKeyPortal(); return; }
        Log.Information("Pause key {Key} received through the portal - video grace pause", CoreSettings.Current.PauseKey);
        try { PauseKeyGracePause(); }
        catch (Exception ex) { Log.Warning(ex, "Pause key: grace pause failed"); }
    }
}
