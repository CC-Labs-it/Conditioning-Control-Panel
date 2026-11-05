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
    [InlineData("png", false, 62.5)]   // late ticks: counting 50 ms per tick reaches only 2400 ms (main a4a5aa43b)
    public void Start_RunsOnTheTracker_GazeScoresTheRound_PanicClosesTheGame(string kind, bool panicInRound, double periodMs = 50)
    {
        // Seed the shared LibVLC so video panes really start players (InlineLoopVideoTests.WithClip).
        if (kind is "mp4" or "mixed")
        {
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
        }
        AvaloniaTestDispatcher.Run(() => Body(kind, panicInRound, periodMs));
    }

    private static void Body(string kind, bool panicInRound, double periodMs)
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
                for (int i = 0; i < (int)Math.Ceiling(3000 / periodMs); i++) { clock.Now += TimeSpan.FromMilliseconds(periodMs); win.RoundTicker_Tick(null, EventArgs.Empty); }
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
