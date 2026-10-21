namespace ConditioningControlPanel.Services;

    public enum WebcamTrackingState
    {
        Stopped,
        Starting,
        Tracking,
        FaceLost,
        CameraInUse,
        CameraDenied,
        Error
    }

public static class WebcamTrackingStatus
{
    public static string LocalizationKey(WebcamTrackingState state) => state switch
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
