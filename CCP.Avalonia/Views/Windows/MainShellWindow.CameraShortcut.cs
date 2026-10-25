using System;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private bool _cameraShortcutBusy;
    private void InitializeCameraShortcut()
    {
        CameraShortcut.Pressed = QueueCameraShortcut;
        KeyDown += (_, e) =>
        {
            // Global route already receives the press on X11/Windows; avoid a double toggle.
            if (CoreSettings.Current.CompanionPrompt?.CameraShortcutGlobal != false && (X11PanicKey.IsListening || Win32PanicKey.IsListening)) return;
            if (!CameraShortcut.Matches(e.Key, e.KeyModifiers)) return;
            e.Handled = true;
            if (CameraShortcut.TryPress()) QueueCameraShortcut();
        };
        KeyUp += (_, e) => { if (e.Key == CameraShortcut.Gesture.Key) CameraShortcut.Release(); };
        Deactivated += (_, _) => CameraShortcut.Release();
        Closed += (_, _) => { if (CameraShortcut.Pressed == QueueCameraShortcut) CameraShortcut.Pressed = null; };
    }
    private void QueueCameraShortcut() => Dispatcher.UIThread.Post(ToggleCameraShortcut);
    private async void ToggleCameraShortcut()
    {
        if (_cameraShortcutBusy || _windowClosed) return;
        _cameraShortcutBusy = true;
        try
        {
            var tracker = WebcamTracker.Instance;
            if (tracker.IsRunning)
            {
                await tracker.StopAsync();
                App.Notifications.Show(Loc.Get("camera_shortcut_toast_stopped"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(2));
                return;
            }
            if (!WebcamConsent.IsCurrent(CoreSettings.Current))
            {
                ShowFromTray();
                var dialog = new Dialogs.WebcamConsentDialog();
                await dialog.ShowDialogSafe(this);
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return;
            }
            if (await tracker.StartAsync())
                App.Notifications.Show(Loc.Get("camera_shortcut_toast_started"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) { Log.Warning(ex, "Camera shortcut failed"); }
        finally { _cameraShortcutBusy = false; }
    }
}
