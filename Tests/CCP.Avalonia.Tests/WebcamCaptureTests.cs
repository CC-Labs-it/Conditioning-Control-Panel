using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamCaptureTests
{
    // COM cannot be exercised on Linux. Keep the port identical to the WPF
    // enumerator, including index order, failed-name fallback and COM release.
    [Fact]
    public void DirectShowEnumeratorRetainsWpfImplementation()
    {
        string root = RepoRoot();
        string wpf = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel/Services/Webcam/WebcamDeviceEnumerator.cs"))
            .Replace("namespace ConditioningControlPanel.Services", "namespace ConditioningControlPanel.Avalonia.Platform")
            .Replace("public static class WebcamDeviceEnumerator", "[System.Runtime.Versioning.SupportedOSPlatform(\"windows\")]\n    internal static class WebcamDeviceEnumerator")
            .Replace("App.Logger?.Warning", "Log.Warning").ReplaceLineEndings("\n");
        Assert.Equal(wpf, File.ReadAllText(Path.Combine(root, "CCP.Avalonia/Platform/WebcamDeviceEnumerator.cs")).ReplaceLineEndings("\n"));
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../.."));

    private sealed class Fake : ICameraCapture
    {
        internal bool Opened = true, Disposed;
        internal int Reads;
        internal Action<Mat, int> Frame = (_, _) => { };
        internal readonly List<(VideoCaptureProperties, double)> Settings = new();
        public bool IsOpened() => Opened;
        public bool Set(VideoCaptureProperties p, double v) { Settings.Add((p, v)); return true; }
        public bool Read(Mat frame) { Frame(frame, ++Reads); return !frame.Empty(); }
        public void Dispose() => Disposed = true;
    }

    private static OpenCvFrameSource Source(Func<int, VideoCaptureAPIs, ICameraCapture> create)
    {
        long time = 0;
        return new OpenCvFrameSource { Windows = true, Create = create,
            Milliseconds = () => time, Wait = ms => time += ms };
    }
    private static void Fill(Mat frame, int value)
    {
        frame.Create(4, 4, MatType.CV_8UC3);
        frame.SetTo(new Scalar(value, value, value));
    }

    [Fact]
    public void CaptureOpensTheSharedSavedIndex()
    {
        var provider = CameraList.Provider;
        int saved = CoreSettings.Current.WebcamDeviceIndex;
        string? device = Environment.GetEnvironmentVariable("CCP_WEBCAM_DEVICE");
        try
        {
            Environment.SetEnvironmentVariable("CCP_WEBCAM_DEVICE", null);
            CameraList.Provider = () => new[] { (0, "A"), (1, "B") };
            CoreSettings.Current.WebcamDeviceIndex = 1;
            int opened = -1;
            using var source = Source((index, _) =>
            {
                opened = index;
                return new Fake { Frame = (m, n) => Fill(m, n * 2) };
            });
            Assert.True(source.Open());
            Assert.Equal(1, opened);
        }
        finally
        {
            CameraList.Provider = provider;
            CoreSettings.Current.WebcamDeviceIndex = saved;
            Environment.SetEnvironmentVariable("CCP_WEBCAM_DEVICE", device);
        }
    }

    [Fact]
    public void WindowsLadderRejectsFlatFeedsAndNegotiatesMjpgBeforeSize()
    {
        var attempts = new List<(int Index, VideoCaptureAPIs Api, Fake Capture)>();
        using var source = Source((index, api) =>
        {
            var capture = new Fake { Frame = (m, n) => Fill(m, attempts.Count == 4 ? n * 2 : 10) };
            attempts.Add((index, api, capture));
            return capture;
        });
        Assert.True(source.Open());
        Assert.Equal(new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.DSHOW,
            VideoCaptureAPIs.MSMF, VideoCaptureAPIs.MSMF }, attempts.Select(a => a.Api));
        Assert.All(attempts.Take(3), a => { Assert.True(a.Capture.Disposed); Assert.Equal(15, a.Capture.Reads); });
        Assert.False(attempts[3].Capture.Disposed);
        Assert.Equal(2, attempts[3].Capture.Reads); // temporal acceptance at the WPF threshold
        Assert.DoesNotContain(attempts[0].Capture.Settings, s => s.Item1 == VideoCaptureProperties.FourCC);
        foreach (int i in new[] { 1, 3 })
        {
            Assert.Equal((VideoCaptureProperties.FourCC, (double)VideoWriter.FourCC('M', 'J', 'P', 'G')), attempts[i].Capture.Settings[0]);
            Assert.Equal((VideoCaptureProperties.FrameWidth, 640d), attempts[i].Capture.Settings[1]);
            Assert.Contains((VideoCaptureProperties.FrameHeight, 480d), attempts[i].Capture.Settings);
            Assert.Contains((VideoCaptureProperties.Fps, 30d), attempts[i].Capture.Settings);
            Assert.Contains((VideoCaptureProperties.BufferSize, 1d), attempts[i].Capture.Settings);
        }
        source.Dispose();
        Assert.True(attempts[3].Capture.Disposed);
    }

    [Fact]
    public void SpatialContrastAcceptsFirstFrameAndStopsTheLadder()
    {
        int opens = 0;
        var fake = new Fake { Frame = (m, _) => { Fill(m, 0); m.Set(0, 0, new Vec3b(255, 255, 255)); } };
        using var source = Source((_, _) => { opens++; return fake; });
        Assert.True(source.Open());
        Assert.Equal(1, opens);
        Assert.Equal(1, fake.Reads);
    }

    [Fact]
    public void StopDuringWarmupReleasesCandidateAndDoesNotTryAnotherBackend()
    {
        bool stopped = false;
        int opens = 0;
        var fake = new Fake();
        using var source = Source((_, _) => { opens++; return fake; });
        var advance = source.Wait;
        source.Wait = ms => { advance(ms); stopped = true; };
        Assert.False(source.Open(() => stopped));
        Assert.Equal(1, opens);
        Assert.True(fake.Disposed);
        Assert.Equal(1, fake.Reads);
    }

    [Theory]
    [InlineData(false, WebcamTrackingState.CameraDenied)]
    [InlineData(true, WebcamTrackingState.CameraInUse)]
    public void ExhaustedLadderDistinguishesDeniedFromUnusableFeed(bool opened, WebcamTrackingState expected)
    {
        using var source = Source((_, _) => new Fake { Opened = opened });
        Assert.False(source.Open());
        Assert.Equal(expected, source.FailureState);
    }

    [Fact]
    public void MissingRuntimeIsReportedAsError()
    {
        using var source = Source((_, _) => throw new DllNotFoundException());
        Assert.False(source.Open());
        Assert.Equal(WebcamTrackingState.Error, source.FailureState);
    }

    [Fact]
    public void FailedAndThrowingBackendsFallThroughAndReleaseHandles()
    {
        int opens = 0;
        var fake = new Fake { Opened = false };
        using var source = Source((_, _) => ++opens == 1 ? throw new InvalidOperationException() : fake);
        Assert.False(source.Open());
        Assert.Equal(4, opens);
        Assert.True(fake.Disposed);
    }
}
