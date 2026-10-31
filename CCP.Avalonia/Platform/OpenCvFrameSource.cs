using System;
using System.Threading;
using ConditioningControlPanel.Services;
using OpenCvSharp;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal interface ICameraCapture : IDisposable
{
    bool IsOpened();
    bool Set(VideoCaptureProperties property, double value);
    bool Read(Mat frame);
}

internal sealed class OpenCvFrameSource : IFrameSource
{
    private sealed class Capture(int index, VideoCaptureAPIs api) : ICameraCapture
    {
        private readonly VideoCapture _capture = new(index, api);
        public bool IsOpened() => _capture.IsOpened();
        public bool Set(VideoCaptureProperties property, double value) => _capture.Set(property, value);
        public bool Read(Mat frame) => _capture.Read(frame);
        public void Dispose() => _capture.Dispose();
    }

    private ICameraCapture? _cap;
    internal WebcamTrackingState FailureState { get; private set; }
    internal Func<int, VideoCaptureAPIs, ICameraCapture> Create = (index, api) => new Capture(index, api);
    internal Func<long> Milliseconds = () => Environment.TickCount64;
    internal Action<int> Wait = Thread.Sleep;
    internal bool Windows = OperatingSystem.IsWindows();

    // WPF WebcamTrackingService.cs:239: default first for each backend, then MJPG.
    public bool Open() => Open(() => false);
    internal bool Open(Func<bool> abandoned)
    {
        FailureState = WebcamTrackingState.CameraDenied;
        int index = int.TryParse(Environment.GetEnvironmentVariable("CCP_WEBCAM_DEVICE"), out var i)
            ? Math.Max(0, i) : ResolveSavedIndex();
        var attempts = Windows
            ? new[] { (VideoCaptureAPIs.DSHOW, false), (VideoCaptureAPIs.DSHOW, true),
                      (VideoCaptureAPIs.MSMF, false), (VideoCaptureAPIs.MSMF, true) }
            : new[] { (OperatingSystem.IsLinux() ? VideoCaptureAPIs.V4L2 : VideoCaptureAPIs.ANY, false) };
        foreach (var (api, mjpg) in attempts)
        {
            if (abandoned()) return false;
            ICameraCapture? candidate = null;
            try
            {
                Log.Information("Webcam: opening camera {Index} via {Api}, MJPG {Mjpg}", index, api, mjpg);
                candidate = Create(index, api);
                if (!candidate.IsOpened()) continue;
                if (FailureState != WebcamTrackingState.Error) FailureState = WebcamTrackingState.CameraInUse;
                // WPF WebcamTrackingService.cs:1379: negotiate format before size.
                if (mjpg) candidate.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                candidate.Set(VideoCaptureProperties.FrameWidth, 640);
                candidate.Set(VideoCaptureProperties.FrameHeight, 480);
                candidate.Set(VideoCaptureProperties.Fps, 30);
                candidate.Set(VideoCaptureProperties.BufferSize, 1);
                if (Windows && !Probe(candidate, abandoned)) continue;
                if (abandoned()) return false;
                _cap = candidate;
                candidate = null;
                return true;
            }
            catch (Exception ex)
            {
                // WPF WebcamTrackingService.cs:1309,1479: runtime failure is not camera denial.
                if (ex is DllNotFoundException or TypeInitializationException || ex.InnerException is DllNotFoundException)
                    FailureState = WebcamTrackingState.Error;
                Log.Warning(ex, "Webcam: camera attempt failed via {Api}", api);
            }
            finally { candidate?.Dispose(); }
        }
        return false;
    }

    // WPF WebcamTrackingService.cs:1407-1465: up to 3 s, 200 ms between reads.
    // Spatial contrast >= 3 or temporal delta >= 2 accepts dim but live feeds.
    private bool Probe(ICameraCapture capture, Func<bool> abandoned)
    {
        using var frame = new Mat();
        using var previous = new Mat();
        using var diff = new Mat();
        long deadline = Milliseconds() + 3000;
        while (Milliseconds() < deadline)
        {
            if (abandoned()) return false;
            if (capture.Read(frame) && !frame.Empty())
            {
                Cv2.MeanStdDev(frame, out _, out var std);
                double delta = 0;
                if (!previous.Empty() && previous.Size() == frame.Size() && previous.Type() == frame.Type())
                {
                    Cv2.Absdiff(frame, previous, diff);
                    var mean = Cv2.Mean(diff);
                    delta = Math.Max(mean.Val0, Math.Max(mean.Val1, mean.Val2));
                }
                if (Math.Max(std.Val0, Math.Max(std.Val1, std.Val2)) >= 3 || delta >= 2) return true;
                frame.CopyTo(previous);
            }
            Wait(200);
        }
        return false;
    }

    internal static int ResolveSavedIndex()
    {
        int saved = CoreSettings.Current.WebcamDeviceIndex;
        var cameras = CameraList.Enumerate();
        foreach (var camera in cameras) if (camera.Index == saved) return saved;
        return cameras.Count > 0 ? cameras[0].Index : Math.Max(0, saved);
    }
    public bool Read(Mat bgr) => _cap != null && _cap.Read(bgr) && !bgr.Empty();
    public void Dispose() { _cap?.Dispose(); _cap = null; }
}
