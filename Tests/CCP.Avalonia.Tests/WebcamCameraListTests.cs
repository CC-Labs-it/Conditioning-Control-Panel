using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamCameraListTests
{
    [Fact]
    public void PickerAndCaptureUseSameListAndPreserveNoncontiguousIndices()
    {
        var saved = (CameraList.Override, CoreSettings.Current.WebcamDeviceIndex);
        try
        {
            CameraList.Override = () => new[] { (3, "Camera A"), (9, "Camera B") };
            CoreSettings.Current.WebcamDeviceIndex = 9;
            Assert.Equal(9, OpenCvFrameSource.ResolveSavedIndex());
            CoreSettings.Current.WebcamDeviceIndex = 7;
            Assert.Equal(3, OpenCvFrameSource.ResolveSavedIndex());
            CameraList.Override = () => Array.Empty<(int, string)>();
            Assert.Equal(7, OpenCvFrameSource.ResolveSavedIndex());
        }
        finally { (CameraList.Override, CoreSettings.Current.WebcamDeviceIndex) = saved; }
    }

    [Fact]
    public void EnumerationFailureReturnsEmptyAndDisabledCaptureNeverTouchesHardware()
    {
        var saved = (CameraList.Override, OpenCvFrameSource.Disabled);
        try
        {
            CameraList.Override = () => throw new InvalidOperationException("synthetic enumeration failure");
            Assert.Empty(CameraList.Enumerate());
            OpenCvFrameSource.Disabled = true;
            using var source = new OpenCvFrameSource();
            Assert.False(source.Open());
        }
        finally { (CameraList.Override, OpenCvFrameSource.Disabled) = saved; }
    }
}
