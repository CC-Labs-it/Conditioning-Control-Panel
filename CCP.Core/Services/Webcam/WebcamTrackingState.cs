namespace ConditioningControlPanel.Services;

// Shared capture states, moved unchanged from WPF WebcamTrackingService.
public enum WebcamTrackingState
{
    Stopped, Starting, Tracking, FaceLost, CameraInUse, CameraDenied, Error
}

public static class WebcamStatus
{
    public static bool IsLive(WebcamTrackingState state) =>
        state is WebcamTrackingState.Tracking or WebcamTrackingState.FaceLost;

    public static string TextKey(WebcamTrackingState state) => state switch
    {
        WebcamTrackingState.Starting => "rf_webcam_starting",
        WebcamTrackingState.Tracking => "rf_webcam_tracking",
        WebcamTrackingState.FaceLost => "rf_webcam_face_lost",
        WebcamTrackingState.CameraInUse => "rf_webcam_camera_in_use",
        WebcamTrackingState.CameraDenied => "rf_webcam_camera_denied",
        WebcamTrackingState.Error => "rf_webcam_error",
        _ => "rf_webcam_stopped",
    };
}
