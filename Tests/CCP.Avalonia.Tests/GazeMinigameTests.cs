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
using Avalonia.Input;
using Avalonia.Input.Raw;
using ConditioningControlPanel.Models;
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
                Assert.Equal("GOOD", win.FindControl<TextBlock>("TxtFeedback")!.Text);
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

    [Theory]
    [InlineData(false, BuiltInMods.CCPDefaultId, null, "GOOD")]
    [InlineData(true, BuiltInMods.CCPDefaultId, null, "GOOD")]
    [InlineData(true, BuiltInMods.DronificationId, null, "COMPLIANT")]
    [InlineData(true, BuiltInMods.DronificationId, "ROUND WON", "ROUND WON")]
    [InlineData(true, BuiltInMods.DronificationId, "", "GOOD")]
    public void CorrectRound_ResolvesLiveModPraise_WithNeutralFallback(
        bool initializeMods, string modId, string? message, string expected) =>
        RunRewardRound(GazeRewardEffect.None, (win, _, score, _) =>
        {
            Assert.Null(AvApp.Mods);
            using var mods = new CoreModsSnapshot();
            var oldProvider = CoreSettings.ServiceProvider;
            var settings = new SettingsService(); // assembly sandbox; no real profile
            ModMessages? messages = null;
            string? previous = null;
            try
            {
                CoreSettings.ServiceProvider = () => settings;
                if (initializeMods)
                {
                    AvApp.StartMods();
                    AvApp.Mods!.ActivateMod(modId); // switch AFTER the round began
                    messages = AvApp.Mods.ActiveMod.Manifest.Messages!;
                    previous = messages.GazeCorrect;
                    if (message != null) messages.GazeCorrect = message;
                }
                score();
                Assert.Equal(expected, win.FindControl<TextBlock>("TxtFeedback")!.Text);
            }
            finally
            {
                if (messages != null) messages.GazeCorrect = previous;
                settings.SealForReset();
                CoreSettings.ServiceProvider = oldProvider;
                AvApp.ResetReleaseContent();
            }
        }, expected);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PackDrag_WaitsForMovement_AndRequiresAHeldLeftPress(bool horizontal) =>
        RunRewardRound(GazeRewardEffect.None, (win, _, _, _) =>
        {
            var card = win.FindControl<WrapPanel>("FocusPanel")!.Children.OfType<Border>().Single();
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var calls = 0;
            PointerPressedEventArgs? pressed = null;
            win.DragPack = (press, _, effects) =>
            {
                Assert.Same(pressed, press); // retained original press, as Avalonia requires
                Assert.Equal(DragDropEffects.Move, effects);
                calls++;
                return Task.FromResult(DragDropEffects.Move);
            };
            void Press(RawInputModifiers button = RawInputModifiers.LeftMouseButton)
            {
                pressed = new PointerPressedEventArgs(card, pointer, win, new global::Avalonia.Point(100, 100), 0,
                    new PointerPointProperties(button, button == RawInputModifiers.LeftMouseButton
                        ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.RightButtonPressed), KeyModifiers.None);
                card.RaiseEvent(pressed);
            }
            void Move(double distance, bool held = true) => card.RaiseEvent(new PointerEventArgs(
                InputElement.PointerMovedEvent, card, pointer, win,
                new global::Avalonia.Point(100 + (horizontal ? distance : 0), 100 + (horizontal ? 0 : distance)), 0,
                new PointerPointProperties(held ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None,
                    PointerUpdateKind.Other), KeyModifiers.None));

            Press();
            Assert.Equal(0, calls);
            Move(1);
            Assert.Equal(0, calls);
            Move(100);
            Assert.Equal(1, calls);
            Move(200);
            Assert.Equal(1, calls);
            Press();
            Move(1, held: false); // releasing before the threshold disarms it
            Move(100);
            Assert.Equal(1, calls);
            Press(RawInputModifiers.RightMouseButton);
            Move(100);
            Assert.Equal(1, calls);
            Press();
            Move(-100);
            Assert.Equal(2, calls);
        });


    private static void RunRewardRound(GazeRewardEffect effect,
        Action<GazeMinigameWindow, SteppedClock, Action, Action> check, string expectedPraise = "GOOD") => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.Calibration,
            CoreAudio.PlayOneShotProvider, CoreSession.IsEngineRunningProvider, s.FlashEnabled, CoreFlash.IsRunning);
        var timing = (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay);
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
                Assert.Equal(expectedPraise, win.FindControl<TextBlock>("TxtFeedback")!.Text);
            }
            void Panic()
            {
                PanicSurfaces.All.Single(x => x.Id == "gaze-minigame").Stop(null);
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
            (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay) = timing;
            File.Delete(Path.Combine(CorePaths.UserData, GazeMinigameSettings.FileName));
            dir.Delete(true);
        }
    });


    [Theory]
    [InlineData(1, 3)]
    [InlineData(1, 0)]
    [InlineData(41, 3)] // 123 source frames: the shared budget samples every second frame
    public Task GifFrames_PreserveUnequalDelays_LongHolds_AndLoopBoundary(int repeats, int firstDelayCs) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var dir = Directory.CreateTempSubdirectory("gaze-gif-timing-");
        var clock = new SteppedClock();
        var oldClock = GazeMinigameWindow.Clock;
        GazeMinigameWindow.Clock = clock;
        try
        {
            var bytes = Convert.FromBase64String(Gif);
            var delays = new[] { firstDelayCs, 7, 80 }; // GIF centiseconds
            var firstMs = firstDelayCs > 0 ? firstDelayCs * 10 : 100;
            var loopMs = firstMs + 70 + 800;
            var frame = 0;
            for (var i = 0; i < bytes.Length - 7; i++)
                if (bytes[i] == 0x21 && bytes[i + 1] == 0xF9 && bytes[i + 2] == 4)
                {
                    bytes[i + 4] = (byte)delays[frame++];
                    bytes[i + 5] = 0;
                }
            Assert.Equal(3, frame);
            if (repeats > 1)
            {
                // Skip the NETSCAPE loop extension; repeat just the three image/control blocks.
                var firstFrame = Enumerable.Range(0, bytes.Length - 2).First(i =>
                    bytes[i] == 0x21 && bytes[i + 1] == 0xF9 && bytes[i + 2] == 4);
                bytes = bytes.Take(firstFrame).Concat(Enumerable.Range(0, repeats)
                    .SelectMany(_ => bytes.Skip(firstFrame).Take(bytes.Length - firstFrame - 1)))
                    .Append((byte)0x3B).ToArray();
            }
            var path = Path.Combine(dir.FullName, "timed.gif");
            File.WriteAllBytes(path, bytes);
            using var image = new GazeRoundImage(path);
            await image.Decoding;
            var first = image.Source;
            if (repeats > 1)
            {
                image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(firstMs + 69));
                Assert.Same(first, image.Source); // sampled frame holds for 30 + 70ms
                image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(firstMs + 70));
                Assert.NotSame(first, image.Source);
                image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(repeats * loopMs - 1));
                Assert.NotSame(first, image.Source);
                image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(repeats * loopMs));
                Assert.Same(first, image.Source); // final sampled frame includes the full tail hold
                return;
            }
            image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(firstMs));
            var second = image.Source;
            Assert.NotSame(first, second);
            image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(firstMs + 69));
            Assert.Same(second, image.Source);
            image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(firstMs + 70));
            var third = image.Source;
            Assert.NotSame(second, third);
            Assert.NotSame(first, third);
            image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(loopMs - 1));
            Assert.Same(third, image.Source);
            image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(loopMs));
            Assert.Same(first, image.Source);
            image.AdvanceFrame(clock.Now + TimeSpan.FromMilliseconds(2 * loopMs + firstMs));
            Assert.Same(second, image.Source); // late ticks retain the absolute loop phase
        }
        finally
        {
            GazeMinigameWindow.Clock = oldClock;
            dir.Delete(true);
        }
    });


    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
