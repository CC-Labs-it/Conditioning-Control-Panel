using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Lab.GazeMinigame;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Lab.GazeMinigame;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Helpers;

namespace CCP.Avalonia.Tests;

/// <summary>Lab gaze minigame: the persisted Focus/Ignore packs reach Start, Start runs on the webcam
/// tracker (a fake frame source, never a camera), the tracker's gaze side scores the round, and panic
/// closes the game. Stepped clock and instant countdown (P08).</summary>
public sealed class GazeMinigameTests
{
    private sealed class BlankSource : IFrameSource
    {
        public bool Open() => true;
        public bool Read(Mat bgr) { bgr.Create(480, 640, MatType.CV_8UC3); bgr.SetTo(Scalar.All(0)); return true; }
        public void Dispose() { }
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // 1x1 PNG
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // Same three-frame fixture as SpiralOverlayTests: red/green/blue, 30ms per frame.
    private const string Gif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    [Theory]
    [InlineData("png", false)]
    [InlineData("mp4", false)]
    [InlineData("gif", false)]
    [InlineData("mixed", false)]
    [InlineData("mp4", true)]
    [InlineData("gif", true)]
    public void Start_RunsOnTheTracker_GazeScoresTheRound_PanicClosesTheGame(string kind, bool panicInRound)
    {
        // Seed the shared LibVLC so video panes really start players (InlineLoopVideoTests.WithClip).
        if (kind is "mp4" or "mixed")
        {
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
        }
        AvaloniaTestDispatcher.Run(() => Body(kind, panicInRound));
    }

    private static void Body(string kind, bool panicInRound)
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var (oldAudio, oldVolume) = (CoreAudio.PlayOneShotProvider, s.MasterVolume);
        var clips = new List<(string Path, float Volume, string Tag)>();
        var (oldClock, oldDelay) = (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay);
        var dir = Directory.CreateTempSubdirectory("gaze-test-");
        var feedback = new TaskCompletionSource();
        var baselinePlayers = InlineLoopVideo.LivePlayers;
        GazeMinigameWindow? win = null;
        try
        {
            CoreAudio.PlayOneShotProvider = (path, volume, tag, _, _) => clips.Add((path, volume, tag));
            s.MasterVolume = 36;
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlankSource();
            tracker.Calibration = new WebcamCalibrationData();
            var clock = new SteppedClock();
            GazeMinigameWindow.Clock = clock;
            GazeMinigameWindow.Delay = t => t.TotalMilliseconds == 1400 ? feedback.Task : Task.CompletedTask;

            string Pack(string name)
            {
                var p = Directory.CreateDirectory(Path.Combine(dir.FullName, name)).FullName;
                if (kind is "png" or "mixed") File.WriteAllBytes(Path.Combine(p, "1.png"), Png);
                if (kind == "gif") File.WriteAllBytes(Path.Combine(p, "1.GIF"), Convert.FromBase64String(Gif));
                if (kind is "mp4" or "mixed")
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), Path.Combine(p, "1.mp4"));
                return p;
            }
            var saved = new GazeMinigameSettings
            {
                ImageCount = 2, VideoCount = 2, PassTimeSec = 3, WrongHoldMs = 2000,
                RewardEffect = GazeRewardEffect.Audio, RewardAudioFile = "bell.wav",
            };
            saved.Packs.Add(new GazePackRef { Path = Pack("focus"), Role = GazePackRole.Focus });
            saved.Packs.Add(new GazePackRef { Path = Pack("noise"), Role = GazePackRole.Ignore });
            saved.Save();

            win = new GazeMinigameWindow();
            win.Show();
            var start = win.FindControl<Button>("BtnStartGame")!;
            Assert.True(start.IsEnabled);                          // the saved roles came back

            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinUntil(() => win.FindControl<Grid>("GameplayScreen")!.IsVisible), "game did not start");
            Assert.True(tracker.IsRunning);                        // Start opened the (fake) camera

            Assert.Contains(kind == "mixed" ? "1 / 4" : "1 / 2", win.FindControl<TextBlock>("TxtRoundInfo")!.Text!);
            var left = win.FindControl<Grid>("LeftPane")!;
            var right = win.FindControl<Grid>("RightPane")!;
            var leftAsset = left.Children[0];
            var rightAsset = right.Children[0];
            if (kind == "mp4" || leftAsset is InlineLoopVideo)
            {
                var leftVideo = Assert.IsType<InlineLoopVideo>(leftAsset);
                var rightVideo = Assert.IsType<InlineLoopVideo>(rightAsset);
                Assert.Equal(Stretch.Uniform, leftVideo.Stretch);
                Assert.False(leftVideo.Loops);
                Assert.False(rightVideo.Loops);
                Assert.Equal(Stretch.Uniform, rightVideo.Stretch);
                Assert.Contains(win.CorrectIsLeftForTest ? "focus" : "noise", leftVideo.ClipPath!);
                Assert.Contains(win.CorrectIsLeftForTest ? "noise" : "focus", rightVideo.ClipPath!);
                Assert.True(leftVideo.HasPlayer && rightVideo.HasPlayer, "video panes started no player");
                Assert.Equal(baselinePlayers + 2, InlineLoopVideo.LivePlayers);
                Assert.InRange(leftVideo.StartOffset!.Value, 0f, 0.5f);   // WPF random start in the first half
                Assert.InRange(rightVideo.StartOffset!.Value, 0f, 0.5f);
            }
            else
            {
                Assert.IsType<GazeRoundImage>(leftAsset);
                Assert.IsType<GazeRoundImage>(rightAsset);
                if (kind == "gif")
                {
                    var image = (GazeRoundImage)leftAsset;
                    Assert.True(SpinUntil(() => image.Decoding.IsCompleted));
                    Assert.True(image.Decoding.IsCompletedSuccessfully);
                    var first = image.Source;
                    // GIFs animate even during the one-second gaze grace period.
                    clock.Now += TimeSpan.FromMilliseconds(50);
                    win.RoundTicker_Tick(null, EventArgs.Empty);
                    Assert.NotSame(first, image.Source);
                }
            }

            if (!panicInRound)
            {
                var correct = win.CorrectIsLeftForTest ? GazeSide.Left : GazeSide.Right;
                clock.Now += TimeSpan.FromMilliseconds(1100); // past the round-start grace
                win.RoundTicker_Tick(null, EventArgs.Empty);
                tracker.RaiseGazeSideForTest(correct);
                for (int i = 0; i < 60; i++) { clock.Now += TimeSpan.FromMilliseconds(50); win.RoundTicker_Tick(null, EventArgs.Empty); }
                Assert.Equal("GOOD GIRL", win.FindControl<TextBlock>("TxtFeedback")!.Text);
                Assert.Equal(new[] { "bell.wav", "chime.wav" }, clips.Select(c => Path.GetFileName(c.Path)));
                Assert.All(clips, c =>
                {
                    Assert.True(File.Exists(c.Path));
                    Assert.Equal((float)Math.Pow(0.36f, 1.5), c.Volume);
                    Assert.Equal("gaze-reward", c.Tag);
                });
            }

            Assert.True(GazeMinigameWindow.IsAnyRunning());        // fullscreen: panic must not arm the exit ladder
            PanicSurfaces.All.Single(x => x.Id == "gaze-minigame").Stop(null);
            Assert.False(win.IsVisible);
            Assert.False(GazeMinigameWindow.IsAnyRunning());
            Assert.Empty(left.Children);
            Assert.Empty(right.Children);
            Assert.Null(((Image)leftAsset).Source);
            Assert.Null(((Image)rightAsset).Source);
            Assert.Equal(baselinePlayers, InlineLoopVideo.LivePlayers);
            // A feedback continuation must not start the second round after panic.
            feedback.SetResult();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Empty(left.Children);
            Assert.Empty(right.Children);
            Assert.False(win.FindControl<Grid>("ResultsScreen")!.IsVisible);
        }
        finally
        {
            win?.Close();
            feedback.TrySetResult();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            tracker.Stop();
            tracker.Calibration = oldCal;
            (CoreAudio.PlayOneShotProvider, s.MasterVolume) = (oldAudio, oldVolume);
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
            (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay) = (oldClock, oldDelay);
            try { File.Delete(Path.Combine(CorePaths.UserData, GazeMinigameSettings.FileName)); } catch { }
            try { dir.Delete(true); } catch { }
        }
    }

    [Fact]
    public void OverlayPulseReward_DoublesBothActiveHeadOverlays_RestoresLiveValues_AndPanicCancels() =>
        RunRewardRound(GazeRewardEffect.OverlayPulse, (win, clock, score, panic) =>
        {
            var s = CoreSettings.Current;
            var old = (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled, s.SpiralOpacity, s.SpiralPath, s.GentleVisuals);
            var hooks = (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests, OverlayPulse.Clock);
            var gif = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
            File.WriteAllBytes(gif, Convert.FromBase64String(Gif));
            using var cleanup = new Callback(() =>
            {
                OverlayPulse.Cancel();
                PinkFilterOverlay.CloseAll();
                SpiralOverlay.CloseAll();
                (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled, s.SpiralOpacity, s.SpiralPath, s.GentleVisuals) = old;
                (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests, OverlayPulse.Clock) = hooks;
                File.Delete(gif);
            });
            CoreSession.IsEngineRunningProvider = () => true;
            (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled, s.SpiralOpacity, s.SpiralPath, s.GentleVisuals) = (true, 50, true, 80, gif, false);
            PinkFilterOverlay.SkipPlatformChecksForTests = SpiralOverlay.SkipPlatformChecksForTests = true;
            OverlayPulse.Clock = clock;
            PinkFilterOverlay.Refresh(win);
            SpiralOverlay.Refresh(win);
            Assert.True(SpinUntil(() => SpiralOverlay.IsShowing));
            var tint = (SolidColorBrush)((Border)PinkFilterOverlay.Shown[0].Content!).Background!;
            score();
            Assert.True(OverlayPulse.IsPulsing);
            Assert.Equal(255, tint.Color.A); // 50% (the setting's max) doubled to 100%
            Assert.Equal(Math.Min(SpiralFrames.Paint(0.8) * 2, 1), SpiralOverlay.Shown[0].Spiral.Opacity, 10);
            Assert.Equal(50, s.PinkFilterOpacity); // pulse must never write settings
            Assert.Equal(80, s.SpiralOpacity);
            s.PinkFilterOpacity = 25;
            s.SpiralOpacity = 40;
            clock.Now += TimeSpan.FromMilliseconds(1000);
            OverlayPulse.Tick();
            Assert.False(OverlayPulse.IsPulsing);
            Assert.Equal((byte)(0.25 * 255), tint.Color.A);
            Assert.Equal(SpiralFrames.Paint(0.4), SpiralOverlay.Shown[0].Spiral.Opacity, 10);
            OverlayPulse.PulseOverlays();
            Assert.True(OverlayPulse.IsPulsing);
            panic();
            Assert.False(OverlayPulse.IsPulsing);
            // Cancellation restores in place; an old tick cannot re-open a stopped overlay.
            PinkFilterOverlay.CloseAll();
            SpiralOverlay.CloseAll();
            clock.Now += TimeSpan.FromSeconds(2);
            OverlayPulse.Tick();
            Assert.False(PinkFilterOverlay.IsShowing || SpiralOverlay.IsShowing);
        });

    [Fact]
    public void OverlayPulse_GentleVisualsEaseAndRestore_AndInactiveServiceShowsNothing() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var providers = (CoreSettings.ServiceProvider, CoreSession.IsEngineRunningProvider, OverlayPulse.Clock);
        var skip = (PinkFilterOverlay.SkipPlatformChecksForTests, PinkFilterOverlay.PulseHold);
        var service = new SettingsService(); // CCP_USERDATA_DIR is the assembly's temporary sandbox
        var s = service.Current;
        var clock = new SteppedClock();
        var host = new global::Avalonia.Controls.Window();
        using var cleanup = new Callback(() =>
        {
            OverlayPulse.Cancel();
            PinkFilterOverlay.CloseAll();
            host.Close();
            (PinkFilterOverlay.SkipPlatformChecksForTests, PinkFilterOverlay.PulseHold) = skip;
            (CoreSettings.ServiceProvider, CoreSession.IsEngineRunningProvider, OverlayPulse.Clock) = providers;
        });
        CoreSettings.ServiceProvider = () => service;
        CoreSession.IsEngineRunningProvider = () => false;
        OverlayPulse.Clock = clock;
        PinkFilterOverlay.SkipPlatformChecksForTests = true;
        PinkFilterOverlay.PulseHold = false;
        s.PinkFilterEnabled = true;
        s.PinkFilterOpacity = 20;
        s.SpiralEnabled = false;
        s.GentleVisuals = true;
        s.GentleFadeSeconds = 0.3;
        host.Show();
        OverlayPulse.PulseOverlays();
        Assert.False(OverlayPulse.IsPulsing || PinkFilterOverlay.IsShowing);
        CoreSession.IsEngineRunningProvider = () => true;
        OverlayPulse.PulseOverlays(); // enabled without an active overlay is still a no-op
        Assert.False(OverlayPulse.IsPulsing || PinkFilterOverlay.IsShowing);
        PinkFilterOverlay.Refresh(host);
        var tint = (SolidColorBrush)((Border)PinkFilterOverlay.Shown[0].Content!).Background!;
        OverlayPulse.PulseOverlays();
        Assert.Equal(51, tint.Color.A);
        clock.Now += TimeSpan.FromMilliseconds(100);
        OverlayPulse.Tick();
        Assert.InRange(tint.Color.A, (byte)52, (byte)101);
        clock.Now += TimeSpan.FromMilliseconds(300);
        OverlayPulse.Tick();
        Assert.Equal(102, tint.Color.A);
        clock.Now += TimeSpan.FromMilliseconds(700);
        OverlayPulse.Tick();
        Assert.InRange(tint.Color.A, (byte)52, (byte)101);
        clock.Now += TimeSpan.FromMilliseconds(200);
        OverlayPulse.Tick();
        Assert.Equal(51, tint.Color.A);
        Assert.False(OverlayPulse.IsPulsing);
        // Takeover's hold is the existing head equivalent of a standalone OverlayService.Start.
        CoreSession.IsEngineRunningProvider = () => false;
        PinkFilterOverlay.PulseHold = true;
        OverlayPulse.PulseOverlays();
        Assert.True(OverlayPulse.IsPulsing);
        PinkFilterOverlay.CloseAll();
        Assert.False(OverlayPulse.IsPulsing); // closing the beat owner cancels without a late restore
    });

    [Fact]
    public void OverlayPulse_ImmediatePeakRestoresLiveSettings_AndStopCancels() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var clock = new SteppedClock();
        var host = new global::Avalonia.Controls.Window();
        var service = new SettingsService();
        var old = (CoreSettings.ServiceProvider, CoreSession.IsEngineRunningProvider, OverlayPulse.Clock,
            PinkFilterOverlay.SkipPlatformChecksForTests);
        using var cleanup = new Callback(() =>
        {
            OverlayPulse.Cancel();
            PinkFilterOverlay.CloseAll();
            host.Close();
            (CoreSettings.ServiceProvider, CoreSession.IsEngineRunningProvider, OverlayPulse.Clock,
                PinkFilterOverlay.SkipPlatformChecksForTests) = old;
        });
        CoreSettings.ServiceProvider = () => service;
        CoreSession.IsEngineRunningProvider = () => true;
        OverlayPulse.Clock = clock;
        PinkFilterOverlay.SkipPlatformChecksForTests = true;
        var s = service.Current;
        s.PinkFilterEnabled = true;
        s.PinkFilterOpacity = 50;
        s.SpiralEnabled = false;
        s.GentleVisuals = false;
        host.Show();
        PinkFilterOverlay.Refresh(host);
        var tint = (SolidColorBrush)((Border)PinkFilterOverlay.Shown[0].Content!).Background!;
        OverlayPulse.PulseOverlays();
        Assert.Equal(255, tint.Color.A);
        Assert.Equal(50, s.PinkFilterOpacity);
        s.PinkFilterOpacity = 25;
        clock.Now += TimeSpan.FromSeconds(1);
        OverlayPulse.Tick();
        Assert.False(OverlayPulse.IsPulsing);
        Assert.Equal((byte)(0.25 * 255), tint.Color.A);
        OverlayPulse.PulseOverlays();
        AvApp.StopDesktopOverlays(final: false);
        Assert.False(OverlayPulse.IsPulsing);
        Assert.Equal((byte)(0.25 * 255), tint.Color.A);
        clock.Now += TimeSpan.FromSeconds(2);
        OverlayPulse.Tick();
        Assert.Equal((byte)(0.25 * 255), tint.Color.A);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GentlePulse_CapturesEndpointsAcrossSettingsAndRampChanges_RestoresLiveValues(bool ramp) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var service = new SettingsService();
        var s = service.Current;
        var clock = new SteppedClock();
        var host = new global::Avalonia.Controls.Window();
        var gif = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(Gif));
        var old = (CoreSettings.ServiceProvider, CoreSession.IsEngineRunningProvider, OverlayPulse.Clock,
            AvApp.Sessions, CoreSession.IsSessionRunningProvider);
        var skip = (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests);
        SessionRunner? runner = null;
        using var cleanup = new Callback(() =>
        {
            OverlayPulse.Cancel();
            PinkFilterOverlay.CloseAll();
            SpiralOverlay.CloseAll();
            runner?.Stop();
            host.Close();
            File.Delete(gif);
            (CoreSettings.ServiceProvider, CoreSession.IsEngineRunningProvider, OverlayPulse.Clock,
                AvApp.Sessions, CoreSession.IsSessionRunningProvider) = old;
            (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests) = skip;
        });
        CoreSettings.ServiceProvider = () => service;
        CoreSession.IsEngineRunningProvider = () => true;
        AvApp.Sessions = null;
        OverlayPulse.Clock = clock;
        PinkFilterOverlay.SkipPlatformChecksForTests = SpiralOverlay.SkipPlatformChecksForTests = true;
        s.PinkFilterEnabled = s.SpiralEnabled = s.GentleVisuals = true;
        s.GentleFadeSeconds = 0.3;
        s.PinkFilterOpacity = s.SpiralOpacity = 20;
        s.SpiralPath = gif;
        if (ramp)
        {
            runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            var session = new ConditioningControlPanel.Models.Session { Id = "pulse_ramp", DurationMinutes = 20 };
            session.Settings.RampCurve = ConditioningControlPanel.Models.RampCurve.Linear;
            session.Settings.PinkFilterEnabled = session.Settings.SpiralEnabled = true;
            session.Settings.PinkFilterStartOpacity = session.Settings.SpiralOpacity = 10;
            session.Settings.PinkFilterEndOpacity = session.Settings.SpiralOpacityEnd = 50;
            runner.Start(session);
            runner.Tick(TimeSpan.FromMinutes(5));
        }
        host.Show();
        PinkFilterOverlay.Refresh(host);
        SpiralOverlay.Refresh(host);
        Assert.True(SpinUntil(() => SpiralOverlay.IsShowing));
        var tint = (SolidColorBrush)((Border)PinkFilterOverlay.Shown[0].Content!).Background!;
        OverlayPulse.PulseOverlays();
        clock.Now += TimeSpan.FromMilliseconds(100);
        OverlayPulse.Tick();
        var before = (tint.Color.A, SpiralOverlay.Shown[0].Spiral.Opacity);
        s.PinkFilterOpacity = s.SpiralOpacity = 2;
        runner?.Tick(TimeSpan.FromMinutes(15));
        PinkFilterOverlay.Refresh(host);
        SpiralOverlay.Refresh(host);
        OverlayPulse.Tick(); // same instant: neither a settings edit nor a moving ramp changes the envelope
        Assert.Equal(before.Item1, tint.Color.A);
        Assert.Equal(before.Item2, SpiralOverlay.Shown[0].Spiral.Opacity, 10);
        clock.Now += TimeSpan.FromMilliseconds(1300);
        OverlayPulse.Tick();
        Assert.False(OverlayPulse.IsPulsing);
        Assert.Equal((byte)((runner?.PinkOpacity ?? 2) / 100.0 * 255), tint.Color.A);
        Assert.Equal(SpiralFrames.Paint((runner?.SpiralOpacity ?? 2) / 100.0), SpiralOverlay.Shown[0].Spiral.Opacity, 10);
    });

    [Fact]
    public void RoundVideoUsesOneShotModeWithoutChangingLoopingAdopters() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var pane = GazeMinigameWindow.CreateRoundVideo("round.mp4");
        Assert.Equal("round.mp4", pane.ClipPath);
        Assert.False(pane.Loops);
        Assert.InRange(pane.StartOffset!.Value, 0f, 0.5f);
        Assert.Equal(Stretch.Uniform, pane.Stretch);
        Assert.True(new InlineLoopVideo("poster.mp4").Loops);
    });

    private sealed class Callback(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private static void RunRewardRound(GazeRewardEffect effect,
        Action<GazeMinigameWindow, SteppedClock, Action, Action> check) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.Calibration,
            CoreAudio.PlayOneShotProvider, CoreSession.IsEngineRunningProvider, s.FlashEnabled, CoreFlash.IsRunning);
        var timing = (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay);
        var stoppedHook = CoreEngine.StoppedHook;
        var clock = new SteppedClock();
        var dir = Directory.CreateTempSubdirectory("gaze-reward-");
        var feedback = new TaskCompletionSource();
        GazeMinigameWindow? win = null;
        try
        {
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlankSource();
            tracker.Calibration = new WebcamCalibrationData();
            // App wires this desktop cleanup through the shell's OnEngineStopped, even when idle.
            CoreEngine.StoppedHook = () => AvApp.StopDesktopOverlays(final: false);
            CoreSession.IsEngineRunningProvider = () => false;
            CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
            s.FlashEnabled = false; // no background timer races; Start still marks the schedule running
            CoreFlash.Start();
            GazeMinigameWindow.Clock = clock;
            GazeMinigameWindow.Delay = t => t.TotalMilliseconds == 1400 ? feedback.Task : Task.CompletedTask;
            var saved = new GazeMinigameSettings
            {
                ImageCount = 2, VideoCount = 0, PassTimeSec = 3, WrongHoldMs = 2000, RewardEffect = effect,
            };
            foreach (var role in new[] { GazePackRole.Focus, GazePackRole.Ignore })
            {
                var path = Directory.CreateDirectory(Path.Combine(dir.FullName, role.ToString())).FullName;
                File.WriteAllBytes(Path.Combine(path, "1.png"), Png);
                saved.Packs.Add(new GazePackRef { Path = path, Role = role });
            }
            saved.Save();
            win = new GazeMinigameWindow();
            win.Show();
            win.FindControl<Button>("BtnStartGame")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinUntil(() => win.FindControl<Grid>("GameplayScreen")!.IsVisible));
            Assert.True(tracker.IsRunning);
            Assert.False(CoreFlash.IsRunning); // Start really suspended the running schedule (#221)
            void Score()
            {
                clock.Now += TimeSpan.FromMilliseconds(1100);
                win.RoundTicker_Tick(null, EventArgs.Empty);
                tracker.RaiseGazeSideForTest(win.CorrectIsLeftForTest ? GazeSide.Left : GazeSide.Right);
                for (int i = 0; i < 60; i++)
                {
                    clock.Now += TimeSpan.FromMilliseconds(50);
                    win.RoundTicker_Tick(null, EventArgs.Empty);
                }
                Assert.Equal("GOOD GIRL", win.FindControl<TextBlock>("TxtFeedback")!.Text);
            }
            void Panic()
            {
                PanicSurfaces.StopAll("test-gaze-reward");
                Assert.False(win.IsVisible);
                feedback.TrySetResult();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Empty(win.FindControl<Grid>("LeftPane")!.Children);
                Assert.False(win.FindControl<Grid>("ResultsScreen")!.IsVisible);
            }
            check(win, clock, Score, Panic);
        }
        finally
        {
            win?.Close();
            feedback.TrySetResult();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            tracker.Stop();
            CoreFlash.Stop();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.Calibration,
                CoreAudio.PlayOneShotProvider, CoreSession.IsEngineRunningProvider, s.FlashEnabled, _) = old;
            if (old.Item8) CoreFlash.Start();
            CoreEngine.StoppedHook = stoppedHook;
            (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay) = timing;
            File.Delete(Path.Combine(CorePaths.UserData, GazeMinigameSettings.FileName));
            dir.Delete(true);
        }
    });

    [Fact]
    public Task GifFrames_LoopOnSteppedClock_AndReleaseOnDispose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var dir = Directory.CreateTempSubdirectory("gaze-gif-");
        var clock = new SteppedClock();
        var oldClock = GazeMinigameWindow.Clock;
        GazeMinigameWindow.Clock = clock;
        GazeRoundImage? image = null;
        try
        {
            var path = Path.Combine(dir.FullName, "round.gif");
            File.WriteAllBytes(path, Convert.FromBase64String(Gif));
            image = new GazeRoundImage(path);
            await image.Decoding;
            var first = image.Source;
            Assert.NotNull(first);
            image.AdvanceFrame(clock.Now += TimeSpan.FromMilliseconds(30));
            var second = image.Source;
            Assert.NotSame(first, second);
            image.AdvanceFrame(clock.Now += TimeSpan.FromMilliseconds(30));
            Assert.NotSame(first, image.Source);
            Assert.NotSame(second, image.Source);
            image.AdvanceFrame(clock.Now += TimeSpan.FromMilliseconds(30));
            Assert.Same(first, image.Source);
            image.Dispose();
            image.AdvanceFrame(clock.Now += TimeSpan.FromSeconds(1));
            Assert.Null(image.Source);
        }
        finally
        {
            image?.Dispose();
            GazeMinigameWindow.Clock = oldClock;
            dir.Delete(true);
        }
    });

    [Fact]
    public Task GifFrames_DisposedDuringDecode_NeverPublishLateFrames() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var dir = Directory.CreateTempSubdirectory("gaze-gif-late-");
        try
        {
            var path = Path.Combine(dir.FullName, "round.gif");
            File.WriteAllBytes(path, Convert.FromBase64String(Gif));
            using var image = new GazeRoundImage(path);
            image.Dispose(); // continuation cannot run until this dispatcher yields
            await image.Decoding;
            image.AdvanceFrame(DateTimeOffset.UtcNow);
            Assert.Null(image.Source);
        }
        finally { dir.Delete(true); }
    });

    [Fact]
    public void RewardClips_AreCopiedToTheHeadsRuntimePaths()
    {
        foreach (var clip in new[] { "chime.wav", "bell.wav", "clicker.mp3", "lock-click.mp3" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "AwarenessPresets", "audio", clip);
            Assert.True(File.Exists(path), "Missing reward clip: " + path);
            Assert.True(new FileInfo(path).Length > 0);
        }
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>Start hops the camera open to the pool (StartAsync); pump the dispatcher until it lands.</summary>
    private static bool SpinUntil(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < 15000)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }
        return done();
    }
}
