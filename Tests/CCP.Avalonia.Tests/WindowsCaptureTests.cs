using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WindowsCaptureTests
{
    private sealed class Capture(string mode) : OpenCvFrameSource.ICapture
    {
        public bool Disposed;
        public int Reads;
        public readonly List<(VideoCaptureProperties, double)> Settings = new();
        public bool IsOpened() => mode is not ("closed" or "dispose");
        public void Set(VideoCaptureProperties key, double value) => Settings.Add((key, value));
        public bool Read(Mat frame)
        {
            Reads++;
            if (mode == "throw") throw new IOException("fake driver");
            if (mode == "empty" || (mode == "warm" && Reads <= 7)) return false;
            frame.Create(8, 8, MatType.CV_8UC3);
            frame.SetTo(Scalar.All(mode == "motion" && Reads > 1 ? 3 : 0));
            if (mode is "good" or "warm") frame.Set(0, 0, new Vec3b(200, 200, 200));
            return true;
        }
        public void Dispose() { Disposed = true; if (mode == "dispose") throw new IOException("fake close"); }
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("dispose")]
    [InlineData("empty")]
    [InlineData("flat")]
    [InlineData("throw")]
    public void RejectedCapturesFallThroughAllOrderedAttemptsAndAreDisposed(string mode)
    {
        long ms = 0;
        var handles = new List<Capture>();
        var apis = new List<VideoCaptureAPIs>();
        using var source = new OpenCvFrameSource
        {
            Now = () => DateTime.UnixEpoch.AddMilliseconds(ms), Delay = n => ms += n,
            Create = (index, api) =>
            {
                Assert.Equal(7, index);
                Assert.All(handles, h => Assert.True(h.Disposed));
                apis.Add(api);
                var h = new Capture(handles.Count == 3 ? "good" : mode);
                handles.Add(h);
                return h;
            }
        };
        Assert.True(source.Open(7, false, true));
        Assert.Equal(new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.MSMF }, apis);
        int mjpg = VideoWriter.FourCC('M', 'J', 'P', 'G');
        Assert.Contains((VideoCaptureProperties.FourCC, (double)mjpg), handles[3].Settings);
        if (mode is not ("closed" or "dispose"))
        {
            Assert.DoesNotContain(handles[0].Settings, p => p.Item1 == VideoCaptureProperties.FourCC);
            Assert.Contains((VideoCaptureProperties.FourCC, (double)mjpg), handles[1].Settings);
        }
        Assert.False(handles[3].Disposed);
        source.Dispose();
        Assert.All(handles, h => Assert.True(h.Disposed));
    }

    [Theory]
    [InlineData("motion", true, 200)]
    [InlineData("warm", true, 1400)]
    [InlineData("flat", false, 12000)]
    public void ProbeAcceptsTemporalChangeButExhaustsTheWarmupForFrozenFeeds(string mode, bool expected, long elapsed)
    {
        long ms = 0;
        var handles = new List<Capture>();
        using var source = new OpenCvFrameSource
        {
            Now = () => DateTime.UnixEpoch.AddMilliseconds(ms), Delay = n => ms += n,
            Create = (_, _) => { var h = new Capture(mode); handles.Add(h); return h; }
        };
        Assert.Equal(expected, source.Open(0, false, true));
        Assert.Equal(expected ? 1 : 4, handles.Count);
        Assert.Equal(elapsed, ms);
        if (!expected) Assert.All(handles, h => Assert.True(h.Disposed));
    }

    [Fact]
    public void AbandonedOpenDisposesTheCandidateAndDoesNotRetry()
    {
        bool stopped = false;
        int creates = 0;
        var handle = new Capture("flat");
        using var source = new OpenCvFrameSource
        {
            Create = (_, _) => { creates++; return handle; },
            Delay = _ => stopped = true, Abandoned = () => stopped
        };
        Assert.False(source.Open(0, false, true));
        Assert.Equal(1, creates);
        Assert.True(handle.Disposed);
    }

    [Fact]
    public void UnnamedDeviceUsesEachShippedTranslation()
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
            {
                loc.SetLanguage(code);
                var path = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                Assert.Equal(json.RootElement.GetProperty("webcam_unnamed_device").GetString(), WindowsCameras.DeviceName(null));
                Assert.Equal(WindowsCameras.DeviceName(null), WindowsCameras.DeviceName(" "));
                Assert.Equal("USB Camera", WindowsCameras.DeviceName("USB Camera"));
            }
        }
        finally { loc.SetLanguage(previous); }
    }
}
