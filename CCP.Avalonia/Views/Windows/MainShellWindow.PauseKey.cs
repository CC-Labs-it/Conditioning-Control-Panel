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

    internal bool PauseKeyWayland = OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    internal Func<bool> PauseKeyVideoPlaying = () => MandatoryVideoOverlay.Instance.Scheduler.IsPlaying;
    internal Func<bool> PauseKeyGracePause = () => MandatoryVideoOverlay.Instance.TryGracePause(fromPanicKey: false);
    internal Func<string, Action, Action, CancellationToken, Task<IDisposable?>> RegisterWaylandPauseKey = PortalPauseShortcut.BindAsync;
    internal bool PauseKeyArmed => _pauseKeyRegistration != null;

    /// <summary>Desktop startup only; render/tests do not arm keys.</summary>
    internal void StartPauseKeyPortal()
    {
        if (!PauseKeyWayland || _pauseKeyWatch != null) return;
        ApplyPauseKeyPortal();
        // Same slow poll as Quick Recal: video start/end, settings replacement and Lockdown all land here.
        _pauseKeyWatch = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => ApplyPauseKeyPortal());
        _pauseKeyWatch.Start();
        Closed += (_, _) => StopPauseKeyPortal();
    }

    internal void StopPauseKeyPortal()
    {
        _pauseKeyWatch?.Stop();
        _pauseKeyWatch = null;
        _pauseKeyBinding = null;
        CancelPauseKeyRegistration();
    }

    /// <summary>WPF OnGlobalKeyPressed: the panic key wins a shared binding (by keysym, as the portal
    /// sees it), Lockdown and either key capture refuse the press.</summary>
    internal static bool PauseKeyRefused(AppSettings s) =>
        string.IsNullOrWhiteSpace(s.PauseKey)
        || PanicPolicy.PauseKeyIsShadowedByPanicKey(s.PanicKey, s.PanicKeyEnabled, s.PauseKey)
        || (s.PanicKeyEnabled && string.Equals(X11PanicKey.XKeyName(s.PanicKey ?? ""), X11PanicKey.XKeyName(s.PauseKey), StringComparison.OrdinalIgnoreCase))
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
        _ = BindWaylandPauseKeyAsync(binding.Item2, _pauseKeyGeneration, _pauseKeyPending.Token);
    }

    private void CancelPauseKeyRegistration()
    {
        _pauseKeyGeneration++;
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
                () => Dispatcher.UIThread.Post(() => { if (generation == _pauseKeyGeneration) OnPortalPauseKeyPressed(); }),
                () => Dispatcher.UIThread.Post(() => { if (generation == _pauseKeyGeneration) CancelPauseKeyRegistration(); }),
                cancellation);
            if (generation != _pauseKeyGeneration || cancellation.IsCancellationRequested) registration?.Dispose();
            else _pauseKeyRegistration = registration;
        }
        catch (Exception ex) { registration?.Dispose(); Log.Warning(ex, "Pause key Wayland registration failed"); }
    }

    /// <summary>UI thread. Rechecked here: settings or Lockdown may have changed since the last poll.</summary>
    internal void OnPortalPauseKeyPressed()
    {
        if (PauseKeyRefused(CoreSettings.Current)) { ApplyPauseKeyPortal(); return; }
        Log.Information("Pause key {Key} received through the portal - video grace pause", CoreSettings.Current.PauseKey);
        try { PauseKeyGracePause(); }
        catch (Exception ex) { Log.Warning(ex, "Pause key: grace pause failed"); }
    }
}
