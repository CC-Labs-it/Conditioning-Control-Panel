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
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Lab.GazeMinigame;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Lab.GazeMinigame;
using ConditioningControlPanel.Models;
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

    [Fact]
    public void FlashReward_ReachesHeadWhileScheduleSuspended_PanicDropsShownAndQueuedFlashes() =>
        RunRewardRound(GazeRewardEffect.Flashes, (win, _, score, panic) =>
        {
            var old = (FlashOverlay.Fire, CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider);
            var bursts = new List<(Visual Host, bool Ambient)>();
            using var cleanup = new Callback(() =>
            {
                (FlashOverlay.Fire, CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider) = old;
                FlashOverlay.CloseAll(final: false);
            });
            FlashOverlay.Fire = (host, ambient) => bursts.Add((host, ambient));
            FlashOverlay.SeedCore(() => win, () => { }); // the same wiring App installs
            Assert.False(CoreFlash.IsRunning);
            score();
            var burst = Assert.Single(bursts);
            Assert.Same(win, burst.Host);
            Assert.False(burst.Ambient); // defaults, including count/duration/size, stay in TriggerOnce
            Assert.False(CoreFlash.IsRunning);
            var flash = new FlashOverlayWindow();
            FlashOverlay.Active.Add((flash, new PixelRect(0, 0, 400, 300)));
            flash.Closed += (_, _) => FlashOverlay.Active.RemoveAll(x => x.Window == flash);
            flash.Show();
            var generation = FlashOverlay.Generation;
            panic();
            Assert.False(flash.IsVisible);
            Assert.Empty(FlashOverlay.Active);
            Assert.True(FlashOverlay.Generation > generation);
            Assert.False(CoreFlash.IsRunning);
        });

    [Fact]
    public void BubblesReward_SpawnsExactlyFiveWithoutAmbientSchedule_PanicClearsField() =>
        RunRewardRound(GazeRewardEffect.Bubbles, (win, clock, score, panic) =>
        {
            var s = CoreSettings.Current;
            var old = (BubbleOverlay.SkipPlatformChecksForTests, s.BubbleSharedHost, s.BubblesEnabled);
            BubbleOverlay.Stop();
            using var cleanup = new Callback(() =>
            {
                BubbleOverlay.Stop();
                (BubbleOverlay.SkipPlatformChecksForTests, s.BubbleSharedHost, s.BubblesEnabled) = old;
            });
            (BubbleOverlay.SkipPlatformChecksForTests, s.BubbleSharedHost, s.BubblesEnabled) = (true, true, false);
            score();
            Assert.Equal(5, BubbleOverlay.Field.Bubbles.Count);
            Assert.False(BubbleOverlay.IsRunning);
            Assert.All(BubbleOverlay.Field.Bubbles, b => Assert.Null(b.Spec));
            // The field's own frame clock cannot add bubbles while the continuous service is off.
            BubbleOverlay.OnFrame(TimeSpan.Zero);
            clock.Now += TimeSpan.FromSeconds(1);
            BubbleOverlay.OnFrame(TimeSpan.FromSeconds(1));
            Assert.Equal(5, BubbleOverlay.Field.Bubbles.Count);
            panic();
            Assert.Empty(BubbleOverlay.Field.Bubbles);
            BubbleOverlay.OnFrame(TimeSpan.FromSeconds(2));
            Assert.Empty(BubbleOverlay.Field.Bubbles);
            Assert.False(BubbleOverlay.IsRunning);
        });

    [Fact]
    public void OneShotBubbles_RespectCap_ReleaseIdleWindows_AndCanHandOverToAmbient() =>
        RunRewardRound(GazeRewardEffect.None, (win, _, score, panic) =>
        {
            var s = CoreSettings.Current;
            var old = (BubbleOverlay.SkipPlatformChecksForTests, s.BubbleSharedHost);
            BubbleOverlay.Stop();
            using var cleanup = new Callback(() =>
            {
                BubbleOverlay.Stop();
                (BubbleOverlay.SkipPlatformChecksForTests, s.BubbleSharedHost) = old;
            });
            (BubbleOverlay.SkipPlatformChecksForTests, s.BubbleSharedHost) = (true, false);
            for (int i = 0; i < 5; i++) BubbleOverlay.SpawnOnce(win);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count); // WPF's non-shared cap
            Assert.NotEmpty(BubbleOverlay.Shown);
            foreach (var bubble in BubbleOverlay.Field.Bubbles) bubble.Y = -100000;
            BubbleOverlay.OnFrame(TimeSpan.Zero);
            Assert.Empty(BubbleOverlay.Field.Bubbles);
            Assert.Empty(BubbleOverlay.Shown);
            BubbleOverlay.SpawnOnce(win);
            var windows = BubbleOverlay.Shown.ToArray();
            BubbleOverlay.Start(win);
            Assert.True(BubbleOverlay.IsRunning);
            Assert.Equal(windows, BubbleOverlay.Shown.ToArray()); // no second set of windows
            Assert.Equal(2, BubbleOverlay.Field.Bubbles.Count); // one-shot + ambient's immediate first spawn
            panic();
            Assert.Empty(BubbleOverlay.Shown);
        });

    private sealed class RewardVoice : MindWipePlayer.IVoice
    {
        public bool Disposed;
        public double Volume { get; set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void MindWipeReward_ReachesStoppedHeadPlayerAtSavedVolume_PanicDisposesVoice() =>
        RunRewardRound(GazeRewardEffect.MindWipe, (win, _, score, panic) =>
        {
            var s = CoreSettings.Current;
            var old = (CoreMindWipe.TriggerOnceProvider, CoreMindWipe.StopProvider, s.MindWipeAudioPath, s.MindWipeVolume);
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            File.WriteAllBytes(file, new byte[] { 0 }); // the head player uses a fake voice, never an audio device
            var voices = new List<RewardVoice>();
            MindWipePlayer? player = null;
            using var cleanup = new Callback(() =>
            {
                player?.Stop();
                (CoreMindWipe.TriggerOnceProvider, CoreMindWipe.StopProvider, s.MindWipeAudioPath, s.MindWipeVolume) = old;
                File.Delete(file);
            });
            (s.MindWipeAudioPath, s.MindWipeVolume) = (file, 37);
            player = new MindWipePlayer((path, volume, loop, ended) =>
            {
                Assert.Equal(file, path);
                Assert.False(loop);
                var voice = new RewardVoice { Volume = volume };
                voices.Add(voice);
                return voice;
            });
            CoreMindWipe.TriggerOnceProvider = player.TriggerOnce;
            CoreMindWipe.StopProvider = player.Stop;
            Assert.False(player.IsRunning);
            score();
            var played = Assert.Single(voices);
            Assert.Equal(0.37, played.Volume, 10);
            win.Close(); // the reward can outlive a normally closed game and an already stopped engine
            Assert.False(played.Disposed);
            panic();
            Assert.True(played.Disposed);
            Assert.False(player.IsRunning);
        });

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
            (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled, s.SpiralOpacity, s.SpiralPath, s.GentleVisuals) = (true, 60, true, 80, gif, false);
            PinkFilterOverlay.SkipPlatformChecksForTests = SpiralOverlay.SkipPlatformChecksForTests = true;
            OverlayPulse.Clock = clock;
            PinkFilterOverlay.Refresh(win);
            SpiralOverlay.Refresh(win);
            Assert.True(SpinUntil(() => SpiralOverlay.IsShowing));
            var tint = (SolidColorBrush)((Border)PinkFilterOverlay.Shown[0].Content!).Background!;
            score();
            Assert.True(OverlayPulse.IsPulsing);
            Assert.Equal(255, tint.Color.A); // 60% doubled, clamped to 100%
            Assert.Equal(Math.Min(SpiralFrames.Paint(0.8) * 2, 1), SpiralOverlay.Shown[0].Spiral.Opacity, 10);
            Assert.Equal(60, s.PinkFilterOpacity); // pulse must never write settings
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
    });

    private sealed class Callback(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

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
