using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Serilog;
using ScreenPoint = Avalonia.Point;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>Where frames come from. The only seam in the webcam engine: tests hand in a fake,
    /// the app an OpenCV capture. Frames are written into the caller's Mat and never leave memory.</summary>
    internal interface IFrameSource : IDisposable
    {
        bool Open();
        bool Read(Mat bgr);
    }

    /// <summary>The Linux twin of WPF WebcamDeviceEnumerator: one entry per V4L2 capture node read from sysfs,
    /// never opening a camera. Nodes whose <c>index</c> is not 0 are the metadata twins a UVC camera also
    /// exposes; listing them would offer a "camera" that yields no frames.</summary>
    internal static class V4l2Cameras
    {
        /// <summary>sysfs root; tests point it at a fake tree.</summary>
        internal static string Root = "/sys/class/video4linux";

        public static IReadOnlyList<(int Index, string Name)> Enumerate()
        {
            var list = new List<(int, string)>();
            try
            {
                if (!Directory.Exists(Root)) return list;
                foreach (var dir in Directory.GetDirectories(Root, "video*"))
                {
                    if (!int.TryParse(Path.GetFileName(dir)["video".Length..], out int n)) continue;
                    var idx = Path.Combine(dir, "index");
                    if (File.Exists(idx) && File.ReadAllText(idx).Trim() != "0") continue;
                    var nameFile = Path.Combine(dir, "name");
                    var name = File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : "";
                    list.Add((n, name.Length > 0 ? name : $"video{n}"));
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam: camera enumeration failed"); }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }
    }

    /// <summary>Capture with WPF's default-format then MJPG probe (WebcamTrackingService.cs:1269-1326,
    /// 1357-1465). Linux uses V4L2 for both attempts; Windows uses DSHOW then MSMF.</summary>
    internal sealed class OpenCvFrameSource : IFrameSource
    {
        private IFrameSource? _capture;
        private readonly Func<VideoCaptureAPIs, bool, IFrameSource> _attempt;
        private readonly int _warmupMs, _intervalMs;
        internal Func<bool> Cancelled = () => false;
        internal bool AnyOpened { get; private set; }

        internal OpenCvFrameSource(Func<VideoCaptureAPIs, bool, IFrameSource>? attempt = null,
            int warmupMs = 3000, int intervalMs = 200)
        {
            _attempt = attempt ?? ((api, mjpg) => new CaptureAttempt(api, mjpg));
            _warmupMs = warmupMs;
            _intervalMs = intervalMs;
        }

        public bool Open()
        {
            var apis = OperatingSystem.IsWindows()
                ? new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF }
                : new[] { OperatingSystem.IsLinux() ? VideoCaptureAPIs.V4L2 : VideoCaptureAPIs.ANY };
            foreach (var api in apis)
                foreach (bool mjpg in new[] { false, true })
                {
                    if (Cancelled()) return false;
                    IFrameSource? source = null;
                    try
                    {
                        source = _attempt(api, mjpg);
                        if (!source.Open()) continue;
                        AnyOpened = true;
                        if (Probe(source)) { _capture = source; source = null; return true; }
                    }
                    catch (Exception ex) { Log.Warning(ex, "Webcam: capture attempt {Api} MJPG={Mjpg} failed", api, mjpg); }
                    finally { source?.Dispose(); }
                }
            return false;
        }

        // A dark but changing feed is valid even when its spatial contrast is low.
        private bool Probe(IFrameSource source)
        {
            using var frame = new Mat();
            using var previous = new Mat();
            using var diff = new Mat();
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < _warmupMs && !Cancelled())
            {
                if (source.Read(frame) && !frame.Empty())
                {
                    Cv2.MeanStdDev(frame, out _, out var std);
                    double spatial = Math.Max(std.Val0, Math.Max(std.Val1, std.Val2));
                    double temporal = 0;
                    if (!previous.Empty() && previous.Size() == frame.Size() && previous.Type() == frame.Type())
                    {
                        Cv2.Absdiff(frame, previous, diff);
                        var mean = Cv2.Mean(diff);
                        temporal = Math.Max(mean.Val0, Math.Max(mean.Val1, mean.Val2));
                    }
                    if (spatial >= 3.0 || temporal >= 2.0) return true;
                    frame.CopyTo(previous);
                }
                if (_intervalMs > 0) Thread.Sleep(_intervalMs);
            }
            return false;
        }

        /// <summary>The saved /dev/videoN when listed, else the first listed camera.</summary>
        internal static int ResolveSavedIndex()
        {
            int saved = CoreSettings.Current.WebcamDeviceIndex;
            var cams = V4l2Cameras.Enumerate();
            foreach (var c in cams) if (c.Index == saved) return saved;
            return cams.Count > 0 ? cams[0].Index : Math.Max(0, saved);
        }

        public bool Read(Mat bgr) => _capture?.Read(bgr) == true;
        public void Dispose() { _capture?.Dispose(); _capture = null; }

        private sealed class CaptureAttempt(VideoCaptureAPIs api, bool mjpg) : IFrameSource
        {
            private VideoCapture? _cap;
            public bool Open()
            {
                int index = int.TryParse(Environment.GetEnvironmentVariable("CCP_WEBCAM_DEVICE"), out var i)
                    ? Math.Max(0, i) : ResolveSavedIndex();
                _cap = new VideoCapture(index, api);
                if (!_cap.IsOpened()) return false;
                if (mjpg) _cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                _cap.Set(VideoCaptureProperties.FrameWidth, 640);
                _cap.Set(VideoCaptureProperties.FrameHeight, 480);
                _cap.Set(VideoCaptureProperties.Fps, 30);
                _cap.Set(VideoCaptureProperties.BufferSize, 1);
                return true;
            }
            public bool Read(Mat bgr) => _cap != null && _cap.Read(bgr) && !bgr.Empty();
            public void Dispose() { _cap?.Release(); _cap?.Dispose(); _cap = null; }
        }
    }

    /// <summary>
    /// The Linux webcam engine: frame source -> Core BlazeFace/FaceMesh/Iris detectors -> Core
    /// BlinkDetector + GazeEngine, the same pipeline WebcamTrackingService.ProcessFrame runs on WPF:
    /// blinks, face lost/found, head pose, raw iris, gaze side and the projected gaze point. Camera
    /// open only between Start and Stop, only with current consent.
    /// Mouth MAR is shared with Core; tongue HSV follows WPF. Gaze lock-on (Core GazeLock) and long
    /// stare (Core LongStareDetector) run as WPF EmitGazeEvents does.
    /// </summary>
    internal sealed class WebcamTracker
    {
        internal static WebcamTracker Instance { get; } = new();

        /// <summary>Tests swap in a fake; the app captures through OpenCV.</summary>
        internal static Func<IFrameSource> SourceFactory = () => new OpenCvFrameSource();

        internal static string ModelDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Models");

        public event Action? OnBlink;
        public event Action? OnMouthOpen;
        public event Action? OnTongueOut;
        /// <summary>Eyes held shut past BlinkDetector.EyesClosedLongMs (WPF OnEyesClosedLong).</summary>
        public event Action? OnEyesClosedLong;
        public event Action? StateChanged;
        private volatile WebcamTrackingState _state = WebcamTrackingState.Stopped;
        public WebcamTrackingState State => _state;

        internal void SetTrackingState(WebcamTrackingState state)
        {
            lock (_gate)
            {
                // A frame finishing after Stop must not relight any capture indicator.
                if (WebcamStatus.IsLive(state) && !IsRunning) return;
                if (!WebcamStatus.IsLive(state) && IsRunning) return;
                if (_state == state) return;
                _state = state;
            }
            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;
        /// <summary>Median-filtered iris vector, what calibration samples (WPF OnRawIris).</summary>
        public event Action<double, double>? OnRawIris;
        public event Action<double, double>? OnHeadPose;
        public event Action<GazeSide>? OnGazeSide;
        /// <summary>Tests: what a classified frame publishes, without a face in front of a camera.</summary>
        internal void RaiseGazeSideForTest(GazeSide side) => OnGazeSide?.Invoke(side);
        internal void RaiseBlinkForTest() => OnBlink?.Invoke();
        /// <summary>Gaze in DIPs of the calibrated monitor; fires only with a calibration.</summary>
        public event Action<ScreenPoint>? OnGazeMove;
        public event Action<ScreenPoint>? OnLongStare;
        private sealed record GazeSnapshot(double X, double Y);
        private volatile GazeSnapshot? _lastPreLockGaze;
        public ScreenPoint? LastPreLockGaze => _lastPreLockGaze is { } p ? new ScreenPoint(p.X, p.Y) : null;
        public void SetGazeAttractor(double x, double y, double radius) => _gazeLock.SetTarget(x, y, radius);
        public void ClearGazeAttractor() => _gazeLock.Clear();
        /// <summary>Start's stage and status text, 1.0 when tracking is up (WPF OnStartupProgress, same
        /// texts); drives the loading splash. A failed start ends with StateChanged and LastError.</summary>
        public event Action<double, string>? OnStartupProgress;
        /// <summary>The last start failed only because a Stop (panic, revoke) overtook it: WPF's Stopped
        /// state, which closes the splash instead of showing an error.</summary>
        internal bool StartWasStopped { get; private set; }
        private void Progress(double p, string status) => Dispatcher.UIThread.Post(() => OnStartupProgress?.Invoke(p, status));

        /// <summary>The saved calibration (WPF's file in the profile folder), read once on first use
        /// as WPF reads it in the service constructor; revoke clears it with the file.</summary>
        public WebcamCalibrationData? Calibration
        {
            get { if (!_calibrationLoaded) { _calibration = WebcamCalibrationData.Load(); _calibrationLoaded = true; } return _calibration; }
            internal set { _calibration = value; _calibrationLoaded = true; }   // internal: tests
        }
        private volatile WebcamCalibrationData? _calibration;
        private volatile bool _calibrationLoaded;

        /// <summary>Face found/lost from the capture thread: only while this run is still current, so a
        /// Stop (panic) that overtakes the frame is never followed by a green Tracking/Face lost.</summary>
        private void PublishIfCurrent(Run? run, WebcamTrackingState state)
        {
            lock (_gate) { if (_run == run) SetTrackingState(state); }
        }

        public bool IsRunning => _run != null;
        /// <summary>Why the last start failed, for the user; null after a good start.</summary>
        public string? LastError { get; private set; }
        /// <summary>Frames whose processing threw (the loop logs and carries on); tests assert zero.</summary>
        internal int FrameErrors;

        /// <summary>One camera session. The loop owns it and releases it when it exits, so a loop
        /// abandoned by a timed-out Stop still closes its own camera when the driver lets go.</summary>
        private sealed class Run
        {
            public volatile bool Stop;
            public IFrameSource? Source;
            public BlazeFaceDetector? Face;
            public FaceMeshDetector? Mesh;
            public IrisDetector? Iris;
            public Thread? Thread;
            public void Release()
            {
                try { Source?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
                Face?.Dispose(); Mesh?.Dispose(); Iris?.Dispose();
                Source = null; Face = null; Mesh = null; Iris = null;
            }
        }

        private Run? _run;
        private Thread? _wedged;
        private volatile Task? _openCleanup;
        internal TimeSpan CameraOpenTimeout = TimeSpan.FromSeconds(90); // WPF WebcamTrackingService.cs:520,1188
        /// <summary>An abandoned capture or open still owns the camera until its cleanup finishes.</summary>
        internal bool IsClosing => _wedged is { IsAlive: true } || _openCleanup is { IsCompleted: false };
        private readonly BlinkDetector _blink = new();
        private readonly MouthOpenDetector _mouth = new();
        // WPF WebcamTrackingService.cs:265-290,2087-2150; capture thread only.
        private static readonly int[] InnerLip = { 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95, 78, 191, 80, 81, 82 };
        private bool _tongueOut;
        private DateTime? _tongueSince;
        private DateTime _lastTongueOut = DateTime.MinValue;
        private readonly GazeEngine _gaze = new();
        private readonly GazeLock _gazeLock = new();
        private readonly LongStareDetector _longStare = new();
        private int _busy;
        /// <summary>Bumped by every Stop (and so by panic). A start that began before it never publishes
        /// its camera: it releases it instead. Guarded by <see cref="_gate"/> with the publish.</summary>
        private int _gen;
        private readonly object _gate = new();

        /// <summary>Off the UI thread, as WPF StartWebcamOffUiThreadAsync: model load and camera
        /// negotiation can take seconds.</summary>
        public Task<bool> StartAsync()
        {
            int gen = Volatile.Read(ref _gen);
            Interlocked.Increment(ref _queued);   // counts as starting before the pool picks it up
            return Task.Run(() => { try { return Start(gen); } finally { Interlocked.Decrement(ref _queued); } });
        }
        private int _queued;
        /// <summary>The generation is bumped before the hop, so a start in flight cannot publish its
        /// camera even if it finishes before the queued Stop runs.</summary>
        public Task StopAsync() { lock (_gate) _gen++; return Task.Run(Stop); }

        /// <summary>A Start is between its consent check and publishing (camera open / model load).</summary>
        internal bool IsStarting => Volatile.Read(ref _busy) != 0 || Volatile.Read(ref _queued) != 0;

        public bool Start() => Start(Volatile.Read(ref _gen));

        private bool Start(int gen)
        {
            if (IsRunning) return true;
            if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
            var run = new Run();
            bool began = false;
            var failureState = WebcamTrackingState.Error;
            try
            {
                LastError = null;
                StartWasStopped = false;
                // WPF WebcamTrackingService.cs:838-843 refuses before any state change: no consent stays Stopped.
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) { LastError = "Webcam consent is not current."; return false; }
                SetTrackingState(WebcamTrackingState.Starting);
                began = true;
                // WPF #743: a loop a timed-out Stop gave up on may still hold the camera.
                if (IsClosing)
                {
                    LastError = "The previous camera session is still closing. Try again in a moment, or restart the app.";
                    return false;
                }
                Progress(0.08, "Preparing eye-tracking engine…");
                try { Cv2.GetVersionString(); }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Webcam] OpenCV native library failed to load");
                    LastError = "Webcam tracking is unavailable: the OpenCV library could not be loaded on this system.";
                    return false;
                }
                // Models before the camera (WPF opens the camera first), so the stages swap places.
                Progress(0.25, "Loading AI models…");
                try
                {
                    run.Face = new BlazeFaceDetector(Path.Combine(ModelDir, "face_detection_short_range.onnx"), Path.Combine(ModelDir, "blazeface_anchors.json"));
                    run.Mesh = new FaceMeshDetector(Path.Combine(ModelDir, "face_landmark.onnx"));
                    run.Iris = new IrisDetector(Path.Combine(ModelDir, "iris_landmark.onnx"));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Webcam] eye-tracking models failed to load");
                    LastError = "Webcam tracking is unavailable: the eye-tracking models could not be loaded.";
                    run.Release();
                    return false;
                }
                Progress(0.55, "Opening camera…");
                run.Source = SourceFactory();
                if (run.Source is OpenCvFrameSource capture) capture.Cancelled = () => gen != Volatile.Read(ref _gen);
                bool opened;
                try
                {
                    // WPF :1164-1205: native Open/Read can wedge. The worker owns its source
                    // after timeout and closes it only after the driver returns, never during Read.
                    var source = run.Source;
                    var opening = Task.Run(source.Open);
                    if (Task.WaitAny(new Task[] { opening }, CameraOpenTimeout) < 0)
                    {
                        run.Source = null;
                        _openCleanup = opening.ContinueWith(t =>
                        {
                            _ = t.Exception; // observe a late fault
                            try { source.Dispose(); } catch (Exception ex) { Log.Warning(ex, "[Webcam] late open cleanup failed"); }
                        }, TaskScheduler.Default);
                        failureState = WebcamTrackingState.CameraInUse;
                        opened = false;
                    }
                    else
                    {
                        opened = opening.GetAwaiter().GetResult();
                        if (!opened) failureState = source is OpenCvFrameSource { AnyOpened: true }
                            ? WebcamTrackingState.CameraInUse : WebcamTrackingState.CameraDenied;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Warning(ex, "[Webcam] access denied");
                    failureState = WebcamTrackingState.CameraDenied;
                    opened = false;
                }
                catch (Exception ex) { Log.Warning(ex, "[Webcam] open threw"); opened = false; }
                if (!opened)
                {
                    if (gen != Volatile.Read(ref _gen)) StartWasStopped = true;
                    LastError = failureState == WebcamTrackingState.CameraInUse
                        ? "Camera unavailable. It may be in use by another app or blocked by camera privacy settings."
                        : "No camera could be opened. Check that a webcam is connected, camera access is allowed, and it is not in use by another app.";
                    run.Release();
                    return false;
                }
                _blink.Reset();
                _mouth.Reset();
                ResetTongueVisibility();
                _gaze.Reset();
                _longStare.Clear();
                run.Thread = new Thread(() => Loop(run)) { IsBackground = true, Name = "WebcamCapture", Priority = ThreadPriority.BelowNormal };
                bool stale;
                lock (_gate)
                {
                    stale = gen != _gen;
                    if (!stale) _run = run;
                }
                if (stale)
                {
                    LastError = "Webcam tracking was stopped before the camera finished opening.";
                    StartWasStopped = true;
                    run.Release();   // outside _gate: a slow driver close must not block Stop/StopAsync
                    return false;
                }
                Progress(0.92, "Starting capture…");
                SetTrackingState(WebcamTrackingState.Tracking);
                run.Thread.Start();
                Progress(1.0, "Ready");
                Log.Information("[Webcam] tracking started");
                return true;
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
                if (began && !IsRunning) SetTrackingState(StartWasStopped ? WebcamTrackingState.Stopped : failureState);
                Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
            }
        }

        /// <summary>WPF WebcamTrackingService.RevokeConsent, all four promises: stop everything using the
        /// camera, drop the calibration, clear the consent record, turn the webcam features off. Seeded as
        /// CoreWebcam.RevokeConsentAction; UI thread (it closes the Blink Trainer overlays).</summary>
        internal static void RevokeConsent()
        {
            Views.Overlays.BlinkTrainerSession.Stop();
            Instance.Stop();
            WebcamCalibrationData.DeleteIfExists();   // WPF ClearCalibration (WebcamTrackingService.cs:1112)
            Instance.Calibration = null;
            var s = CoreSettings.Current;
            s.WebcamConsentGiven = false;
            s.WebcamConsentVersion = "";
            s.WebcamConsentDate = null;
            s.WebcamCalibrated = false;
            s.WebcamCalibrationMode = "";
            s.WebcamTriggersEnabled = false;
            s.FocusGameEnabled = false;
            CoreSettings.Save();
        }

        public void Stop()
        {
            Run? run;
            lock (_gate) { _gen++; run = Interlocked.Exchange(ref _run, null); }
            SetTrackingState(WebcamTrackingState.Stopped);
            if (run == null) return;
            run.Stop = true;
            // WPF Stop: bounded join; a wedged driver must not hang the caller forever. The loop
            // releases its own camera whenever it does exit; until then Start refuses.
            if (!run.Thread!.Join(TimeSpan.FromSeconds(5)))
            {
                _wedged = run.Thread;
                Log.Warning("[Webcam] capture thread did not exit in 5s; abandoned, it closes the camera when it exits");
            }
            Log.Information("[Webcam] tracking stopped");
            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        private void Loop(Run run)
        {
            try
            {
                using var frame = new Mat();
                int fails = 0;
                while (!run.Stop)
                {
                    // Consent revoked mid-session (or during Start): close the camera now.
                    if (!WebcamConsent.IsCurrent(CoreSettings.Current)) { LastError = "Webcam consent was revoked."; break; }
                    if (!run.Source!.Read(frame))
                    {
                        // WPF MaxConsecutiveReadFails: a camera that stopped delivering ends tracking.
                        if (++fails >= 30) { LastError = "The camera stopped delivering frames."; break; }
                        Thread.Sleep(20);
                        continue;
                    }
                    fails = 0;
                    if (run.Stop) break;
                    try { ProcessFrame(run, frame); }
                    catch (Exception ex) { Interlocked.Increment(ref FrameErrors); Log.Warning(ex, "[Webcam] frame processing threw"); Thread.Sleep(50); }
                }
            }
            finally
            {
                run.Release();
                // Ended on its own (read failures, revoke): no longer running.
                if (Interlocked.CompareExchange(ref _run, null, run) == run)
                {
                    SetTrackingState(LastError == null ? WebcamTrackingState.Stopped : WebcamTrackingState.Error);
                    Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
                }
            }
        }

        /// <summary>WPF ApplyCalibration: save to the profile file, then use it. False (nothing
        /// changed) when the save failed.</summary>
        public bool ApplyCalibration(WebcamCalibrationData data)
        {
            if (!data.Save()) return false;
            SetCalibrationLive(data);
            Log.Information("[Webcam] calibration applied (mode={Mode})", data.Mode);
            return true;
        }

        /// <summary>WPF SetCalibrationLive: in memory only (calibration's verify phase); null clears.</summary>
        public void SetCalibrationLive(WebcamCalibrationData? data)
        {
            Calibration = data;
            _gaze.ResetSideHysteresis();
        }

        /// <summary>WPF Quick Recal's SetRuntimeOffset: swap the whole calibration (the capture thread
        /// reads it every frame), optionally saving it. Null clears the offset.</summary>
        public void SetRuntimeOffset(RuntimeOffsetData? offset, bool persist)
        {
            lock (_gate)
            {
                if (Calibration is not { } current) return;
                var updated = current.WithRuntimeOffset(offset);
                if (persist) updated.Save();
                Calibration = updated;
            }
            Log.Information("[Webcam] runtime offset {State} (persist={Persist})", offset == null ? "cleared" : "set", persist);
        }

        /// <summary>WPF ApplyGazeTrim: fold a bubble-test correction into the calibration's GazeTrim
        /// (Core GazeTrimData.Compose), optionally saving it.</summary>
        public void ApplyGazeTrim(double x0, double x1, double y0, double y1, double centerX, double centerY, bool persist)
        {
            GazeTrimData composed;
            lock (_gate)
            {
                if (Calibration is not { } current) return;
                composed = GazeTrimData.Compose(current.GazeTrim, x0, x1, y0, y1, centerX, centerY, DateTime.UtcNow);
                var updated = current.WithGazeTrim(composed);
                if (persist) updated.Save();
                Calibration = updated;
            }
            Log.Information("[Webcam] gaze trim applied: total x0={X0:F0} x1={X1:F3} y0={Y0:F0} y1={Y1:F3} (persist={Persist})",
                composed.X0, composed.X1, composed.Y0, composed.Y1, persist);
        }

        /// <summary>WPF ProcessFrame: face, mesh, iris, blink, mouth and tongue, then gaze.</summary>
        private void ProcessFrame(Run run, Mat bgr)
        {
            var found = run.Face!.Detect(bgr);
            if (found is not { } f0) { NoFace(run); return; }
            int x = Math.Clamp(f0.X, 0, bgr.Width - 1), y = Math.Clamp(f0.Y, 0, bgr.Height - 1);
            var r = new Rect(x, y, Math.Clamp(f0.Width, 0, bgr.Width - x), Math.Clamp(f0.Height, 0, bgr.Height - y));
            if (r.Width < 16 || r.Height < 16) { NoFace(run); return; }
            var lm = run.Mesh!.Detect(bgr, r);
            if (lm == null) { NoFace(run); return; }
            if (_gaze.FaceSeen())
            {
                PublishIfCurrent(run, WebcamTrackingState.Tracking);
                Dispatcher.UIThread.Post(() => OnFaceFound?.Invoke());
            }
            if (HeadPose(_gaze, lm, bgr.Width, bgr.Height) is { } pose)
                Dispatcher.UIThread.Post(() => OnHeadPose?.Invoke(pose.Yaw, pose.Pitch));
            var left = run.Iris!.Detect(bgr, lm[FaceMeshDetector.LeftEyeOuterIdx], lm[FaceMeshDetector.LeftEyeInnerIdx], isRightEye: false);
            var right = run.Iris.Detect(bgr, lm[FaceMeshDetector.RightEyeOuterIdx], lm[FaceMeshDetector.RightEyeInnerIdx], isRightEye: true);
            if (left == null && right == null) return;
            if (left != null && right != null)
            {
                var ev = _blink.Update(
                    BlinkDetector.ComputeEar(left.Contour, BlinkDetector.IrisContourEarIndices),
                    BlinkDetector.ComputeEar(right.Contour, BlinkDetector.IrisContourEarIndices), DateTime.UtcNow);
                if (ev == BlinkEvent.Blink) Dispatcher.UIThread.Post(() => OnBlink?.Invoke());
                else if (ev == BlinkEvent.EyesClosedLong) Dispatcher.UIThread.Post(() => OnEyesClosedLong?.Invoke());
            }
            var now = DateTime.UtcNow;
            ProcessMouthLandmarks(lm, now, run);   // WPF :1755-1780: after blink, skipped without either iris
            ProcessTongueLandmarks(bgr, lm, now, run);
            (double Dx, double Dy)? vl = left == null ? null
                : _gaze.NormalizeIris(left.IrisCenter, lm[FaceMeshDetector.LeftEyeOuterIdx], lm[FaceMeshDetector.LeftEyeInnerIdx], rightEye: false);
            (double Dx, double Dy)? vr = right == null ? null
                : _gaze.NormalizeIris(right.IrisCenter, lm[FaceMeshDetector.RightEyeOuterIdx], lm[FaceMeshDetector.RightEyeInnerIdx], rightEye: true);
            if (_gaze.CombineEyes(vl, vr) is not { } v || _blink.EyesClosed) return;   // eyes closed: hold, as WPF

            var (dx, dy) = _gaze.PreFilter(v.Dx, v.Dy);
            Dispatcher.UIThread.Post(() => OnRawIris?.Invoke(dx, dy));
            var mapped = _gaze.Step(Calibration, dx, dy, GazeEngine.Now(), out var side);
            Dispatcher.UIThread.Post(() => OnGazeSide?.Invoke(side));
            if (mapped is not { } m) return;
            EmitProjectedGaze(m.X, m.Y, DateTime.UtcNow);
        }

        /// <summary>WPF EmitGazeEvents' final stages. Also feeds synthetic projected points in tests.</summary>
        internal void EmitProjectedGaze(double x, double y, DateTime now)
        {
            _lastPreLockGaze = new GazeSnapshot(x, y); // accuracy uses the uncontracted projection
            var locked = _gazeLock.Apply(x, y);
            var p = _gaze.Follow(locked.X, locked.Y);
            Dispatcher.UIThread.Post(() => OnGazeMove?.Invoke(new ScreenPoint(p.X, p.Y)));
            if (_longStare.Push(now, p.X, p.Y) is { } stare)
                Dispatcher.UIThread.Post(() => OnLongStare?.Invoke(new ScreenPoint(stare.X, stare.Y)));
        }

        /// <summary>WPF UpdateHeadPose: solvePnP here (OpenCvSharp 4.13 takes SolvePnPMethod, see
        /// GazeEngine), model, Euler extraction and smoothing in Core.</summary>
        internal static (double Yaw, double Pitch)? HeadPose(GazeEngine gaze, float[][] lm, int w, int h)
        {
            try
            {
                var pts = gaze.HeadPoseImagePoints(lm);
                if (pts == null) return null;
                using var cam = new Mat(3, 3, MatType.CV_64FC1);
                var k = GazeEngine.HeadPoseCameraMatrix(w, h);
                for (int i = 0; i < 9; i++) cam.Set(i / 3, i % 3, k[i]);
                using var dist = new Mat(4, 1, MatType.CV_64FC1, Scalar.All(0));
                using var obj = InputArray.Create(GazeEngine.HeadPoseModelPoints);
                using var img = InputArray.Create(pts);
                using var rvec = new Mat();
                using var tvec = new Mat();
                Cv2.SolvePnP(obj, img, cam, dist, rvec, tvec, useExtrinsicGuess: false, flags: SolvePnPMethod.Iterative);
                if (rvec.Empty() || rvec.Total() < 3) { gaze.HeadPoseFailed(); return null; }
                using var rot = new Mat();
                Cv2.Rodrigues(rvec, rot);
                return gaze.UpdateHeadPose(rot.At<double>(2, 0), rot.At<double>(2, 1), rot.At<double>(2, 2));
            }
            catch (Exception ex) { gaze.HeadPoseFailed(); Log.Debug("[Webcam] head pose failed: {Error}", ex.Message); return null; }
        }

        internal bool ProcessMouthLandmarks(float[][] landmarks, DateTime now) => ProcessMouthLandmarks(landmarks, now, _run);

        /// <summary>The frame's own capture run tags the event, so a stop drops it before delivery.</summary>
        private bool ProcessMouthLandmarks(float[][] landmarks, DateTime now, Run? run)
        {
            bool fired = _mouth.Update(MouthOpenDetector.ComputeMar(landmarks), now, CoreSettings.Current.WebcamSensitivity);
            if (fired) Dispatcher.UIThread.Post(() => { if (run != null && ReferenceEquals(_run, run)) OnMouthOpen?.Invoke(); });
            return fired;
        }

        // WPF WebcamTrackingService.cs:2087-2150: strict thresholds, 350 ms hold, 700 ms cooldown.
        internal bool ProcessTongueFrame(Mat bgr, float[][] landmarks, bool mouthOpen, DateTime now)
        {
            if (!mouthOpen) { ResetTongueVisibility(); return false; }
            double ratio = ComputeTongueRatio(bgr, landmarks);
            bool nowOut = _tongueOut ? ratio > .18 : ratio > .28;
            bool fired = false;
            if (nowOut && !_tongueOut) _tongueSince = now;
            else if (nowOut && _tongueSince is { } since && _lastTongueOut < since
                && (now - since).TotalMilliseconds >= 350 && (now - _lastTongueOut).TotalMilliseconds >= 700)
            { _lastTongueOut = now; fired = true; }
            else if (!nowOut && _tongueOut) _tongueSince = null;
            _tongueOut = nowOut;
            return fired;
        }

        private void ResetTongueVisibility() { _tongueOut = false; _tongueSince = null; }

        private void ProcessTongueLandmarks(Mat bgr, float[][] landmarks, DateTime now, Run run)
        {
            if (ProcessTongueFrame(bgr, landmarks, _mouth.IsOpen, now))
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ReferenceEquals(_run, run)) return;
                    BarkHead.Engine?.Raise("TongueOut"); // WPF BarkService.cs:662-663
                    OnTongueOut?.Invoke();
                });
        }

        // WPF WebcamTrackingService.cs:2153-2222: only pixels inside the clipped inner-lip polygon;
        // shadow and teeth are excluded from the denominator, fewer than eight valid pixels rejects.
        internal static double ComputeTongueRatio(Mat bgr, float[][] landmarks)
        {
            var points = new Point[InnerLip.Length];
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                int x = (int)Math.Round(landmarks[InnerLip[i]][0]), y = (int)Math.Round(landmarks[InnerLip[i]][1]);
                points[i] = new Point(x, y);
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            minX = Math.Max(0, minX); minY = Math.Max(0, minY);
            maxX = Math.Min(bgr.Width - 1, maxX); maxY = Math.Min(bgr.Height - 1, maxY);
            int width = maxX - minX + 1, height = maxY - minY + 1;
            if (width < 4 || height < 4) return 0;
            for (int i = 0; i < points.Length; i++) points[i] = new Point(points[i].X - minX, points[i].Y - minY);
            using var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
            Cv2.FillPoly(mask, new[] { points }, Scalar.All(255));
            using var crop = new Mat(bgr, new Rect(minX, minY, width, height));
            using var hsv = new Mat();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
            int total = width * height, tongue = 0, other = 0;
            var colors = new byte[total * 3];
            var included = new byte[total];
            System.Runtime.InteropServices.Marshal.Copy(hsv.Data, colors, 0, colors.Length);
            System.Runtime.InteropServices.Marshal.Copy(mask.Data, included, 0, included.Length);
            for (int i = 0; i < total; i++)
            {
                if (included[i] == 0) continue;
                int h = colors[3 * i], s = colors[3 * i + 1], v = colors[3 * i + 2];
                if (v < 40 || (v >= 200 && s <= 50)) continue;
                if ((h <= 20 || h >= 160) && s >= 30 && v >= 40) tongue++;
                else other++;
            }
            int valid = tongue + other;
            return valid < 8 ? 0 : (double)tongue / valid;
        }

        /// <summary>Tests call it bare; the capture thread passes its run so a stale run never publishes.</summary>
        internal void NoFace() => NoFace(null);

        private void NoFace(Run? run)
        {
            if (_gaze.FaceMissing())
            {
                PublishIfCurrent(run, WebcamTrackingState.FaceLost);
                Dispatcher.UIThread.Post(() => OnFaceLost?.Invoke());
            }
            _blink.CancelClosure();
            _mouth.FaceMissing();
            ResetTongueVisibility();
            if (_gaze.NoFaceFrames > GazeEngine.FaceLostFramesThreshold * 2) _longStare.Clear();
        }
    }
}
