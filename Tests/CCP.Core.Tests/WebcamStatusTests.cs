using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;

public sealed class WebcamStatusTests
{
    [Theory]
    [InlineData(WebcamTrackingState.Stopped, "rf_webcam_stopped", false)]
    [InlineData(WebcamTrackingState.Starting, "rf_webcam_starting", false)]
    [InlineData(WebcamTrackingState.Tracking, "rf_webcam_tracking", true)]
    [InlineData(WebcamTrackingState.FaceLost, "rf_webcam_face_lost", true)]
    [InlineData(WebcamTrackingState.CameraInUse, "rf_webcam_camera_in_use", false)]
    [InlineData(WebcamTrackingState.CameraDenied, "rf_webcam_camera_denied", false)]
    [InlineData(WebcamTrackingState.Error, "rf_webcam_error", false)]
    public void StatesHaveSharedLocalizedCopyAndOnlyOpenCaptureStatesAreLive(WebcamTrackingState state, string key, bool live)
    {
        Assert.Equal(key, WebcamStatus.TextKey(state));
        Assert.Equal(live, WebcamStatus.IsLive(state));
    }
}
