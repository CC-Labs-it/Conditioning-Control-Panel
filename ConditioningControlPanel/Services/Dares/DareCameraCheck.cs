using System;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>The app's webcam: <c>App.Webcam</c> and the player's hand-stop flag.</summary>
public sealed class AppDareCameraProbe : IDareCameraProbe
{
    public bool Running => App.Webcam?.IsRunning == true;

    public bool FaceSeen => App.Webcam?.State == WebcamTrackingState.Tracking;

    public bool HeldOff => App.Settings?.Current?.CameraHeldOff == true;

    public bool TryStart()
    {
        var cam = App.Webcam;
        if (cam == null || cam.IsRunning) return cam != null;
        if (!WebcamTrackingService.IsConsentCurrent()) return false;
        _ = Task.Run(() =>
        {
            try { cam.Start(); }
            catch (Exception ex) { App.Logger?.Debug("Dares camera start failed: {E}", ex.Message); }
        });
        return true;
    }

    public void Stop()
    {
        var cam = App.Webcam;
        // Focus Gaze may have come to rely on it meanwhile: then it stays open.
        if (cam == null || App.Settings?.Current?.FocusGazeEnabled == true) return;
        _ = cam.StopAsync();
    }
}
