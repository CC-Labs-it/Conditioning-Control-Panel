using System;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private static Action? _stopLevelUpSound;
    private bool _calibrationNoticeChecked;

    private void InitializeShellNotifications()
    {
        // WPF MainWindow_Loaded: once per launch (Opened re-fires on every tray Hide/Show).
        Opened += (_, _) =>
        {
            if (_calibrationNoticeChecked) return;
            _calibrationNoticeChecked = true;
            try
            {
                OfferLegacyCalibrationNotice(WebcamTracker.Instance.Calibration, App.Notifications, async () =>
                {
                    try { await WebcamCalibrationWindow.ShowDialogWithRecalibrate(this); }
                    catch (Exception ex) { Log.Warning(ex, "Recalibrate toast: failed to open calibration window"); }
                });
            }
            catch (Exception ex) { Log.Debug(ex, "Recalibrate-suggest check failed"); }
        };
        Closed += (_, _) => StopLevelUpSound();
    }

    internal static void OfferLegacyCalibrationNotice(WebcamCalibrationData? calibration,
        NotificationService notices, Action recalibrate)
    {
        if (calibration?.MonitorBounds is not { } bounds || !string.IsNullOrEmpty(bounds.DeviceName)) return;
        notices.ShowSticky("recalibrate-multimonitor",
            "Your calibration needs updating for multi-monitor support.", NotificationType.Warning,
            actionLabel: "Recalibrate", action: recalibrate);
    }

    // WPF MainWindow.xaml.cs PlayLevelUpSound: same file precedence and volume curve/floor.
    internal static void PlayLevelUpSound(string? baseDirectory = null)
    {
        try
        {
            var root = baseDirectory ?? AppContext.BaseDirectory;
            var path = new[]
            {
                Path.Combine(root, "Resources", "sounds", "lvup.mp3"),
                Path.Combine(root, "Resources", "lvlup.mp3"),
                Path.Combine(root, "assets", "sounds", "lvlup.mp3"),
            }.FirstOrDefault(File.Exists);
            if (path == null) return; // WPF keeps the previous clip if no replacement exists.
            StopLevelUpSound();
            var volume = (float)Math.Pow(CoreSettings.Current.MasterVolume / 100f, 1.5) * 0.2625f;
            _stopLevelUpSound = CoreAudio.PlayStoppable(path, Math.Max(0.01f, volume), "level-up");
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to play level up sound"); }
    }

    internal static void StopLevelUpSound()
    {
        try { Interlocked.Exchange(ref _stopLevelUpSound, null)?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "Failed to stop level up sound"); }
    }
}
