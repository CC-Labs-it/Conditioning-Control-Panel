using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private bool _fypEyeSubscribed, _fypOwnsCamera;
    internal Func<DateTime> FypEyeClock = () => DateTime.UtcNow;
    private DateTime _fypLastGaze = DateTime.MinValue;
    internal Task? FypEyeTask;
    // Hardware/modal boundaries only: tests never open a camera or a real consent window.
    internal Func<bool> FypCameraRunning = () => WebcamTracker.Instance.IsRunning;
    internal Func<Task<bool>> FypStartCamera = () => WebcamTracker.Instance.StartAsync();
    internal Func<Task> FypStopCamera = () => WebcamTracker.Instance.StopAsync();
    // The tracker is shared across feed visits. WPF FypHostService.cs:1019 stops synchronously.
    private static Task _fypCameraStop = Task.CompletedTask;
    // WPF FypHostService.cs:928 shares its startup guard across every feed visit.
    private static readonly SemaphoreSlim FypEyeStartup = new(1, 1);
    internal Func<Task<bool>>? FypConsent;
    internal Func<Task>? FypCalibration;

    private async Task<bool> GetFypConsent()
    {
        if (FypConsent != null) return await FypConsent();
        if (WebcamConsent.IsCurrent(CoreSettings.Current)) return true;
        var dialog = new WebcamConsentDialog();
        return await dialog.ShowDialogSafe<bool>(this) && dialog.ConsentGiven;
    }

    // WPF FypHostService.cs:926 consent, background startup and late-close cleanup.
    private async Task EnableFypEyes()
    {
        // Queue rather than drop: an enable during a pending cleanup stop must run once that stop ends.
        await FypEyeStartup.WaitAsync();
        try
        {
            if (_fypEyeSubscribed || _closing) return;
            await Task.Yield(); // leave the browser callback before opening a modal dialog
            await _fypCameraStop; // never attach to a camera whose previous stop is still queued
            if (_closing || !CoreSettings.Current.FypEyeControl) return;
            if (!await GetFypConsent()) { FailFypEyes("consent"); return; }
            if (_closing || !CoreSettings.Current.FypEyeControl) return;
            if (!FypCameraRunning())
            {
                PostFypEyeStatus("starting");
                if (!await FypStartCamera())
                {
                    FailFypEyes(WebcamTracker.Instance.State == WebcamTrackingState.Error ? "error" : "no-camera");
                    return;
                }
                _fypOwnsCamera = true;
            }
            // Closing / toggling off during Start must return the camera we acquired.
            if (_closing || !CoreSettings.Current.FypEyeControl) { DisableFypEyes(); return; }
            var camera = WebcamTracker.Instance;
            camera.OnBlink += FypBlink;
            camera.OnEyesClosedLong += FypEyesClosed;
            // ponytail: one subscription per enabled visit; gaze mode/calibration are gated in the handler.
            camera.OnGazeMove += FypGaze;
            _fypEyeSubscribed = true;
            PostFypEyeStatus(null);
        }
        catch (Exception ex) { Log.Warning("[FYP] eye enable failed: {E}", ex.Message); FailFypEyes("error"); }
        finally
        {
            // A closed visit must finish returning its late camera before the next visit starts.
            try { await _fypCameraStop; }
            finally { FypEyeStartup.Release(); }
        }
    }

    private void FailFypEyes(string reason)
    {
        DisableFypEyes();
        if (_closing) return; // a stale visit cannot turn off the current visit's saved setting
        CoreSettings.Current.FypEyeControl = false;
        PostFypEyeStatus(reason);
    }

    // WPF FypHostService.cs:998 release subscriptions and only the camera we started.
    private void DisableFypEyes()
    {
        var camera = WebcamTracker.Instance;
        if (_fypEyeSubscribed)
        {
            camera.OnBlink -= FypBlink;
            camera.OnEyesClosedLong -= FypEyesClosed;
            camera.OnGazeMove -= FypGaze;
        }
        _fypEyeSubscribed = false;
        if (_fypOwnsCamera) _fypCameraStop = FypStopCamera();
        _fypOwnsCamera = false;
    }

    // WPF FypHostService.cs:1092 and :1098 gesture frames.
    internal void FypBlink() { if (_fypEyeSubscribed && !_closing) Post(new { type = "blink" }); }
    internal void FypEyesClosed() { if (_fypEyeSubscribed && !_closing) Post(new { type = "eyesClosed" }); }

    // WPF FypHostService.cs:1071 native calibration over the feed.
    private async Task CalibrateFypEyes()
    {
        await Task.Yield();
        if (_closing || WebcamCalibrationWindow.IsShowing) return;
        try
        {
            if (FypCameraRunning())
            {
                if (FypCalibration != null) await FypCalibration();
                else await WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
            }
            if (!_closing) PostFypEyeStatus(null);
        }
        catch (Exception ex) { Log.Warning("[FYP] calibration failed: {E}", ex.Message); }
    }

    // WPF FypHostService.cs:1117 mixed-DPI conversion and 100 ms message cap.
    internal void FypGaze(Point point)
    {
        if (!_fypEyeSubscribed || _closing || !CoreSettings.Current.FypEyeGaze
            || WebcamTracker.Instance.Calibration is not { } calibration) return;
        var bounds = calibration.MonitorBounds;
        var now = FypEyeClock();
        if ((now - _fypLastGaze).TotalMilliseconds < 100 || Web.Bounds.Width <= 0 || Web.Bounds.Height <= 0) return;
        var origin = Web.PointToScreen(default);
        var gaze = FypRules.GazePoint(point.X, point.Y, bounds?.DpiScale ?? 1, bounds?.X ?? 0, bounds?.Y ?? 0,
            bounds?.DeviceName != null, origin.X, origin.Y, RenderScaling, Web.Bounds.Width, Web.Bounds.Height);
        _fypLastGaze = now;
        Post(new { type = "gaze", x = gaze.X, y = gaze.Y, inside = gaze.Inside });
    }
}
