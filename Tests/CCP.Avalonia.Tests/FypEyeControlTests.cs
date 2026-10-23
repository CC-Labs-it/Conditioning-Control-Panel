using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// For You eye control (WPF FypHostService:906-1146): consent first, then the camera, then
/// blink / eyesClosed / gaze frames, with an eyeStatus frame for every step. The tracker is a fake:
/// no test opens a camera. Swaps process-wide seams and opens a game window, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FypEyeControlTests
{
    private sealed class FakeEye : IFypEye
    {
        public bool IsRunning { get; set; }
        public bool Calibrated { get; set; }
        public bool Faulted { get; set; }
        public bool StartAnswer = true;
        public int Starts, Stops;
        public (double OriginX, double OriginY, double Scale) CalSpace => (0, 0, 1);
        public event Action? OnBlink;
        public event Action? OnEyesClosedLong;
        public event Action<Point>? OnGazeMove;
        public TaskCompletionSource<bool>? PendingStart;
        public async Task<bool> StartAsync() { Starts++; IsRunning = PendingStart == null ? StartAnswer : await PendingStart.Task; return IsRunning; }
        public void Stop() { Stops++; IsRunning = false; }
        public void Blink() => OnBlink?.Invoke();
        public void CloseEyes() => OnEyesClosedLong?.Invoke();
        public void Gaze(Point p) => OnGazeMove?.Invoke(p);
        public bool HasGazeListener => OnGazeMove != null;
        public bool HasBlinkListener => OnBlink != null;
    }

    private static void Run(FakeEye eye, Func<Window, Task<bool>> consent, Action<GameWindow, List<JObject>> body, Func<Window, Task>? calibrate = null)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var oldService = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var oldGate = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreProgression.AddXPProvider);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.HasLabProvider = () => false;
            CoreProgression.AddXPProvider = (_, _) => { };
            var oldSource = GameWindow.FypEyeSource;
            var oldConsent = GameWindow.FypEyeConsent;
            var oldCalibrate = GameWindow.FypEyeCalibrate;
            var oldCalibrating = GameWindow.FypEyeCalibrating;
            var root = Directory.CreateTempSubdirectory("fyp-eye-").FullName;
            using var server = new WebAssetServer(root);
            var oldServer = GameWindow.Server;
            var oldAssets = CorePaths.EffectiveAssetsProvider;
            GameWindow.Server = () => server;
            CorePaths.EffectiveAssetsProvider = () => root;
            var oldClock = GameWindow.FypEyeClock;
            var s = CoreSettings.Current;
            GameWindow? w = null;
            try
            {
                GameWindow.FypEyeSource = () => eye;
                GameWindow.FypEyeConsent = consent;
                GameWindow.FypEyeCalibrate = calibrate ?? (_ => Task.CompletedTask);
                GameWindow.FypEyeCalibrating = () => false;
                s.FypEyeControl = false;
                s.FypEyeGaze = false;
                s.WebcamConsentGiven = false;
                s.WebcamConsentVersion = "";

                w = GameWindow.Launch(GameWindow.FypId)!;
                Dispatcher.UIThread.RunJobs();
                var posted = new List<JObject>();
                w.Posted += json => posted.Add(JObject.Parse(json));
                w.HandleMessage("{\"type\":\"ready\"}");
                posted.Clear();
                body(w, posted);
            }
            finally
            {
                try { w?.Close(); Dispatcher.UIThread.RunJobs(); } catch { }
                GameWindow.FypEyeSource = oldSource;
                GameWindow.FypEyeConsent = oldConsent;
                GameWindow.FypEyeCalibrate = oldCalibrate;
                GameWindow.FypEyeCalibrating = oldCalibrating;
                GameWindow.Server = oldServer;
                CorePaths.EffectiveAssetsProvider = oldAssets;
                GameWindow.FypEyeClock = oldClock;
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreProgression.AddXPProvider) = oldGate;
                CoreSettings.ServiceProvider = oldService;
                Directory.Delete(root, true);
            }
        });
    }

    private sealed class DeadCamera : ConditioningControlPanel.Avalonia.Platform.IFrameSource
    {
        public bool Throws;
        public bool Open() => Throws ? throw new InvalidOperationException("driver") : false;
        public bool Read(OpenCvSharp.Mat bgr) => false;
        public void Dispose() { }
    }

    [Fact]
    public void TheAppsOwnEye_TellsAMissingCameraFromAFault() => AvaloniaTestDispatcher.Run(() =>
    {
        // WPF :1246: no camera to open reads "no-camera"; only an engine fault reads "error".
        var tracker = ConditioningControlPanel.Avalonia.Platform.WebcamTracker.Instance;
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, ConditioningControlPanel.Avalonia.Platform.WebcamTracker.SourceFactory);
        try
        {
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = ConditioningControlPanel.Services.Webcam.WebcamConsent.ConsentVersion;
            var camera = new DeadCamera();
            ConditioningControlPanel.Avalonia.Platform.WebcamTracker.SourceFactory = () => camera;

            Assert.False(tracker.Start());
            Assert.StartsWith("No camera could be opened", tracker.LastError);
            Assert.True(tracker.StartFoundNoCamera);
            Assert.False(GameWindow.TrackerFypEye.Instance.Faulted);     // the page hears "no-camera"

            camera.Throws = true;
            Assert.False(tracker.Start());
            Assert.False(tracker.StartFoundNoCamera);
            Assert.True(GameWindow.TrackerFypEye.Instance.Faulted);      // the page hears "error"
        }
        finally
        {
            tracker.Stop();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, ConditioningControlPanel.Avalonia.Platform.WebcamTracker.SourceFactory) = old;
        }
    });

    private static void Settle(GameWindow w)
    {
        for (var i = 0; i < 20 && !w.FypEyeTask.IsCompleted; i++) Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FypEyeTask.IsCompleted);
    }

    private static void Toggle(GameWindow w, string key, bool on) =>
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"" + key + "\",\"value\":" + (on ? "true" : "false") + "}");

    [Fact]
    public void ConsentRefused_NeverTouchesTheCamera_AndTheToggleGoesBackOff()
    {
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(false), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal(0, eye.Starts);
            var status = posted.Single();
            Assert.Equal("eyeStatus", (string?)status["type"]);
            Assert.Equal("consent", (string?)status["reason"]);
            Assert.False((bool)status["enabled"]!);
            Assert.False(CoreSettings.Current.FypEyeControl);
            Assert.False(eye.HasBlinkListener);
        });
    }

    [Fact]
    public void ConsentGiven_StartsTheCamera_ThenBlinksAndClosedEyesReachThePage()
    {
        var eye = new FakeEye();
        var asked = 0;
        Run(eye, _ => { asked++; return Task.FromResult(true); }, (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal(1, asked);
            Assert.Equal(1, eye.Starts);
            Assert.Equal(new[] { "starting", null }, posted.Select(p => (string?)p["reason"]));
            Assert.True((bool)posted[1]["enabled"]!);
            Assert.True((bool)posted[1]["running"]!);
            Assert.False((bool)posted[1]["calibrated"]!);

            posted.Clear();
            eye.Blink();
            eye.CloseEyes();
            Assert.Equal(new[] { "blink", "eyesClosed" }, posted.Select(p => (string?)p["type"]));

            // Off: the listeners go, the camera this window started is stopped, the page hears it.
            posted.Clear();
            Toggle(w, "eyeControl", false);
            Assert.False(eye.HasBlinkListener);
            Assert.Equal(1, eye.Stops);
            Assert.False((bool)posted.Single()["enabled"]!);
            eye.Blink();
            Assert.Single(posted);
        });
    }

    [Fact]
    public void ACameraThatWillNotStart_SaysWhy()
    {
        var none = new FakeEye { StartAnswer = false };
        Run(none, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal("no-camera", (string?)posted.Last()["reason"]);
            Assert.False(CoreSettings.Current.FypEyeControl);
        });
        var broken = new FakeEye { StartAnswer = false, Faulted = true };
        Run(broken, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal("error", (string?)posted.Last()["reason"]);
        });
    }

    [Fact]
    public void ACameraSomeoneElseStarted_IsNotStoppedByTheFeed()
    {
        var eye = new FakeEye { IsRunning = true };
        Run(eye, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal(0, eye.Starts);
            Toggle(w, "eyeControl", false);
            Assert.Equal(0, eye.Stops);
            Assert.True(eye.IsRunning);
            Toggle(w, "eyeControl", true);
            Settle(w);
            w.Close();
            Assert.Equal(0, eye.Stops);
        });
    }

    [Fact]
    public void Gaze_IsWiredOnlyWithGazeModeAndACalibration_AndIsThrottled()
    {
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Toggle(w, "eyeGaze", true);
            Assert.False(eye.HasGazeListener);   // no calibration: the page falls back to a random tile

            // The page's Calibrate button: the dialog runs, then gaze is wired and the page hears it.
            posted.Clear();
            var calibrated = 0;
            GameWindow.FypEyeCalibrate = _ => { calibrated++; eye.Calibrated = true; return Task.CompletedTask; };
            w.HandleMessage("{\"type\":\"calibrate\"}");
            Dispatcher.UIThread.RunJobs();
            Wait(w.FypCalibrationTask);
            Assert.Equal(1, calibrated);
            Assert.True(eye.HasGazeListener);
            Assert.True((bool)posted.Last()["calibrated"]!);

            posted.Clear();
            var clock = new EyeClock();
            GameWindow.FypEyeClock = clock;
            eye.Gaze(new Point(10, 10));
            Assert.Equal("gaze", (string?)Assert.Single(posted)["type"]);
            clock.Now += TimeSpan.FromMilliseconds(99);
            eye.Gaze(new Point(20, 20));
            Assert.Single(posted);
            clock.Now += TimeSpan.FromMilliseconds(1);
            eye.Gaze(new Point(30, 30));
            Assert.Equal(2, posted.Count);
            Assert.All(posted, p => Assert.InRange((double)p["x"]!, 0, 1));

            Toggle(w, "eyeGaze", false);
            Assert.False(eye.HasGazeListener);
        });
    }

    [Fact]
    public void ClosingTheFeed_HandsTheCameraBack()
    {
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(true), (w, _) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, eye.Stops);
            Assert.False(eye.HasBlinkListener);
        });
    }

    private sealed class EyeClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-23T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void PersistedEyeControl_RearmsOnReady_WithoutDuplicateSubscriptions()
    {
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(true), (w, posted) =>
        {
            CoreSettings.Current.FypEyeControl = true;
            w.HandleMessage("{\"type\":\"ready\"}");
            Settle(w);
            Assert.True((bool)posted.First()["settings"]!["eyeControl"]!);
            w.HandleMessage("{\"type\":\"ready\"}");
            Settle(w);
            posted.Clear();
            eye.Blink();
            Assert.Equal("blink", (string?)Assert.Single(posted)["type"]);
            Assert.Equal(1, eye.Starts);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartInFlight_PanicOrToggleOff_CannotLeaveACameraOrListeners(bool panic)
    {
        var eye = new FakeEye { PendingStart = new() };
        Run(eye, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Dispatcher.UIThread.RunJobs();
            Toggle(w, "eyeControl", true);
            Assert.Equal(1, eye.Starts);
            if (panic) PanicSurfaces.All.Single(s => s.Id == "games").Stop(null);
            else Toggle(w, "eyeControl", false);
            eye.PendingStart.SetResult(true);
            Settle(w);
            Assert.False(eye.IsRunning);
            Assert.False(eye.HasBlinkListener);
            Assert.False(eye.HasGazeListener);
            Assert.Equal(1, eye.Stops);
        });
    }

    [Fact]
    public void PanicWhileConsentIsPending_NeverStartsTheCamera()
    {
        var eye = new FakeEye();
        var consent = new TaskCompletionSource<bool>();
        Run(eye, _ => consent.Task, (w, _) =>
        {
            Toggle(w, "eyeControl", true);
            Dispatcher.UIThread.RunJobs();
            PanicSurfaces.All.Single(s => s.Id == "games").Stop(null);
            consent.SetResult(true);
            Settle(w);
            Assert.Equal(0, eye.Starts);
            Assert.False(eye.HasBlinkListener);
        });
    }

    [Fact]
    public void CurrentConsent_StartsWithoutOpeningAnotherDialog()
    {
        var currentConsent = GameWindow.FypEyeConsent;
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(true), (w, _) =>
        {
            CoreSettings.Current.WebcamConsentGiven = true;
            CoreSettings.Current.WebcamConsentVersion = ConditioningControlPanel.Services.Webcam.WebcamConsent.ConsentVersion;
            GameWindow.FypEyeConsent = currentConsent;
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Empty(w.OwnedWindows);
            Assert.Equal(1, eye.Starts);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GamesPanic_ClosesTheRealFeedOwnedConsentOrCalibrationDialog(bool calibration)
    {
        var consentDialog = GameWindow.FypEyeConsent;
        var calibrationDialog = GameWindow.FypEyeCalibrate;
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(true), (w, _) =>
        {
            if (!calibration) GameWindow.FypEyeConsent = consentDialog;
            Toggle(w, "eyeControl", true);
            Dispatcher.UIThread.RunJobs();
            if (calibration)
            {
                Settle(w);
                GameWindow.FypEyeCalibrate = calibrationDialog;
                w.HandleMessage("{\"type\":\"calibrate\"}");
                Dispatcher.UIThread.RunJobs();
            }
            var dialog = Assert.Single(w.OwnedWindows);
            Assert.True(dialog.IsVisible);
            PanicSurfaces.All.Single(s => s.Id == "games").Stop(null);
            Assert.False(dialog.IsVisible);
            Settle(w);
            Wait(w.FypCalibrationTask);
            Assert.False(eye.IsRunning);
            Assert.False(eye.HasBlinkListener);
        });
    }

    [Fact]
    public void Calibration_BorrowsAnAlreadyRunningCameraWithoutEnablingEyeControl()
    {
        var eye = new FakeEye { IsRunning = true };
        var calls = 0;
        Run(eye, _ => Task.FromResult(true), (w, posted) =>
        {
            w.HandleMessage("{\"type\":\"calibrate\"}");
            Wait(w.FypCalibrationTask);
            Assert.Equal(1, calls);
            Assert.True((bool)posted.Last()["running"]!);
            Assert.False((bool)posted.Last()["enabled"]!);
            Assert.Equal(0, eye.Starts);
            w.Close();
            Assert.Equal(0, eye.Stops);
        }, _ => { calls++; return Task.CompletedTask; });
    }

    [Fact]
    public void Calibration_RequiresRunningCamera_AndDoesNotReenter()
    {
        var eye = new FakeEye();
        var done = new TaskCompletionSource();
        var calls = 0;
        Run(eye, _ => Task.FromResult(true), (w, _) =>
        {
            w.HandleMessage("{\"type\":\"calibrate\"}");
            Wait(w.FypCalibrationTask);
            Assert.Equal(0, calls);
            Toggle(w, "eyeControl", true);
            Settle(w);
            w.HandleMessage("{\"type\":\"calibrate\"}");
            var first = w.FypCalibrationTask;
            Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"calibrate\"}");
            Wait(w.FypCalibrationTask);
            w.HandleMessage("{\"type\":\"calibrate\"}");
            Wait(w.FypCalibrationTask);
            Assert.Equal(1, calls);
            done.SetResult();
            Wait(first);
        }, _ => { calls++; return done.Task; });
    }
}
