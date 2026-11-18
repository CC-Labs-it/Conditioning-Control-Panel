using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

// No camera, COM activation or Avalonia platform needed. Assembly isolation restores settings.
public sealed class WindowsCameraTests
{
    [Fact]
    public void WindowsPickerKeepsDirectShowIndicesAndDuplicateNames()
    {
        var old = V4l2Cameras.Root;
        try
        {
            V4l2Cameras.Root = V4l2Cameras.DefaultRoot;
            var cameras = V4l2Cameras.EnumerateForPlatform(true, () => new[]
            {
                new WindowsCameras.WebcamDevice(0, "USB Camera"),
                new WindowsCameras.WebcamDevice(1, "USB Camera")
            });
            Assert.Equal(new[] { (0, "USB Camera"), (1, "USB Camera") }, cameras);
        }
        finally { V4l2Cameras.Root = old; }
    }

    [Fact]
    public void EnumerationFailureReturnsEmptyButSuccessKeepsItsList()
    {
        var cameras = new[] { new WindowsCameras.WebcamDevice(0, "Camera") };
        Assert.Same(cameras, WindowsCameras.EnumerateOrEmpty(() => cameras));
        Assert.Empty(WindowsCameras.EnumerateOrEmpty(() => throw new InvalidOperationException("fake driver")));
    }

    [Fact]
    public void PickerFallsBackOnlyWhenDirectShowIsEmpty()
    {
        var mf = new[] { new WindowsCameras.WebcamDevice(0, "First"), new WindowsCameras.WebcamDevice(1, "Second") };
        var ds = new[] { new WindowsCameras.WebcamDevice(0, "DirectShow") };
        Assert.Equal(mf, WindowsCameras.EnumerateWithFallback(() => Array.Empty<WindowsCameras.WebcamDevice>(), () => mf));
        Assert.Same(ds, WindowsCameras.EnumerateWithFallback(() => ds, () => throw new Exception()));
        Assert.Empty(WindowsCameras.EnumerateWithFallback(() => Array.Empty<WindowsCameras.WebcamDevice>(), () => throw new TimeoutException()));
        Assert.Empty(WindowsCameras.EnumerateWithFallback(() => Array.Empty<WindowsCameras.WebcamDevice>(), () => throw new InvalidOperationException()));
    }

    [Fact]
    public void QuestProbeUsesStrictWindowsListsAndLinuxProbe()
    {
        WindowsCameras.WebcamDevice[] empty = [];
        WindowsCameras.WebcamDevice[] one = [new(0, "Camera")];
        bool Probe(Func<System.Collections.Generic.IReadOnlyList<WindowsCameras.WebcamDevice>> ds,
            Func<System.Collections.Generic.IReadOnlyList<WindowsCameras.WebcamDevice>> mf) =>
            WindowsCameras.CameraPresent(true, ds, mf, () => throw new Exception());
        Assert.True(Probe(() => one, () => throw new Exception()));
        Assert.True(Probe(() => empty, () => one));
        Assert.False(Probe(() => empty, () => empty));
        Assert.Throws<InvalidOperationException>(() => Probe(() => throw new InvalidOperationException(), () => empty));
        Assert.Throws<TimeoutException>(() => Probe(() => empty, () => throw new TimeoutException()));
        Assert.True(WindowsCameras.CameraPresent(false, () => throw new Exception(), () => throw new Exception(), () => true));
        Assert.False(WindowsCameras.CameraPresent(false, () => throw new Exception(), () => throw new Exception(), () => false));
    }

    [Theory]
    [InlineData(true, false, VideoCaptureAPIs.V4L2)]
    [InlineData(false, true, VideoCaptureAPIs.DSHOW)]
    [InlineData(false, false, VideoCaptureAPIs.ANY)]
    public void CaptureUsesTheEnumeratorsIndexSpace(bool linux, bool windows, VideoCaptureAPIs expected) =>
        Assert.Equal(expected, OpenCvFrameSource.Attempts(linux, windows)[0].Api);

    [Fact]
    public void FakeSysfsStillOverridesWindowsAndSavedSelectionUsesNodeIndices()
    {
        var old = V4l2Cameras.Root;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            V4l2Cameras.Root = root;
            Directory.CreateDirectory(Path.Combine(root, "video4"));
            File.WriteAllText(Path.Combine(root, "video4", "index"), "0");
            File.WriteAllText(Path.Combine(root, "video4", "name"), "Fake Camera");
            Assert.Equal(new[] { (4, "Fake Camera") },
                V4l2Cameras.EnumerateForPlatform(true, () => throw new Exception("must not enumerate COM")));
            CoreSettings.Current.WebcamDeviceIndex = 4;
            Assert.Equal(4, OpenCvFrameSource.ResolveSavedIndex());
            CoreSettings.Current.WebcamDeviceIndex = 99;
            Assert.Equal(4, OpenCvFrameSource.ResolveSavedIndex());
        }
        finally
        {
            V4l2Cameras.Root = old;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void NonWindowsNeverActivatesCom()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Empty(WindowsCameras.EnumerateStrict());
        Assert.Empty(WindowsCameras.Enumerate());
    }
}
