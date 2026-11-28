using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Services.Speech;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class SpeakPlayerTests
{
    private sealed class Audio : IDeeperLocalAudio
    {
        public double PositionSeconds { get; set; }
        public double DurationSeconds => 20;
        public double? PlaybackDurationSeconds => 20;
        public int Volume { get; set; }
        public bool Playing;
        public event Action? Ended;
        public event Action? Failed;
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Dispose() { }
    }

    // Owns the fake transport and its temp file; Dispose restores DeeperLocalAudio.Open.
    private sealed class Media : IDisposable
    {
        public readonly Audio Audio = new();
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        private readonly Func<string, Task<IDeeperLocalAudio?>> _old = DeeperLocalAudio.Open;
        public Media() { File.WriteAllBytes(Path, new byte[16]); DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(Audio); }
        public void Dispose() { DeeperLocalAudio.Open = _old; File.Delete(Path); }
    }

    private static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name,
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(o, args);
    private static void Tick(EnhancementPlayerWindow p) => Call(p, "UiTimer_Tick", null, EventArgs.Empty);
    private static SpeakPromptRuntime Runtime(Func<string, RecognizeOptions, CancellationToken, Task<PhraseResult>> listen,
        Action? stopListening = null, Action<string>? show = null, Action? hide = null, Action<string>? flash = null,
        Func<TimeSpan, Action, Action>? startCueTimer = null) =>
        new(() => true, () => true, () => Task.FromResult(true), () => false, () => { }, () => { }, () => false,
            listen, stopListening ?? (() => { }), show ?? (_ => { }), hide ?? (() => { }), flash, startCueTimer);

    private sealed class Actions : IActionDispatcher, IEnhancementRunCleanup
    {
        public int Calls, Resets;
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext context, CancellationToken token = default)
        { Calls++; return Task.CompletedTask; }
        public void ResetOverlayBands() => Resets++;
        public void StopOneShotFlashes() { }
        public void StopOneShotSubliminals() { }
    }

    [Fact]
    public Task PlayerDispatchesSupportedRulesAndRetiresOnReplacement() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var media = new Media();
        var actions = new Actions();
        var player = new EnhancementPlayerWindow(null, null) { PlayerDispatcherFactory = () => actions };
        try
        {
            player.Show(); player.OpenLocalMediaFile(media.Path);
            for (int i = 0; i < 500 && !media.Audio.Playing; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.True(media.Audio.Playing);
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*", Rules = {
                new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 }, Action = new ConditioningControlPanel.Models.Deeper.PlayAudioAction { Path = "test" } },
                new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 1 }, Action = new TriggerEffectAction { EffectType = EffectTypes.Overlay, OverlayKind = OverlayKinds.PinkFilter } }
            } }, "test");
            Tick(player); media.Audio.PositionSeconds = 1.2; Tick(player);
            Assert.Equal(2, actions.Calls);
            Tick(player); Assert.Equal(2, actions.Calls);
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, "replacement");
            Assert.Equal(1, actions.Resets);
            media.Audio.PositionSeconds = 2; Tick(player);
            Assert.Equal(2, actions.Calls);
        }
        finally { player.Close(); }
    });

    [Fact]
    public Task PlayerDispatchesOverlayTimelineAndStopsItOnReplacement() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var media = new Media();
        var actions = new Actions();
        var player = new EnhancementPlayerWindow(null, null) { PlayerDispatcherFactory = () => actions };
        try
        {
            player.Show(); player.OpenLocalMediaFile(media.Path);
            for (int i = 0; i < 500 && !media.Audio.Playing; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.True(media.Audio.Playing);
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*", TimelineItems = {
                new TimelineItem { Kind = TimelineItemKind.Effect, Start = 1, Duration = 4, EffectType = EffectTypes.Overlay,
                    EffectOverlayKind = OverlayKinds.Spiral, EffectActivation = EffectActivation.Region }
            } }, "test");
            Tick(player); media.Audio.PositionSeconds = 1.2; Tick(player);
            Assert.True(actions.Calls > 0);
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, "replacement");
            Assert.Equal(1, actions.Resets);
        }
        finally { player.Close(); }
    });

    [Fact]
    public void OverlayBandsKeepVoiceAndOtherRuns() => AvaloniaTestDispatcher.Run(() =>
    {
        var host = new global::Avalonia.Controls.Window();
        var first = new object(); var second = new object();
        try
        {
            SpiralOverlay.SetVoiceHold(host, true);
            PinkFilterOverlay.SetVoiceHold(host, true);
            var spiralVoice = SpiralOverlay.VoiceOpacity;
            var pinkVoice = PinkFilterOverlay.VoiceOpacity;
            SpiralOverlay.SetSustained(null, first, "a", 0.8);
            SpiralOverlay.SetSustained(null, second, "b", 0.4);
            PinkFilterOverlay.SetSustained(null, first, "a", 0.8);
            PinkFilterOverlay.SetSustained(null, second, "b", 0.4);
            SpiralOverlay.SetSustained(null, first, "a", 0.2);
            PinkFilterOverlay.SetSustained(null, first, "a", 0.2);
            Assert.Equal(0.4, SpiralOverlay.DeeperOpacity);
            Assert.Equal(0.4, PinkFilterOverlay.DeeperOpacity);
            SpiralOverlay.SetSustained(null, second, "b", null);
            PinkFilterOverlay.SetSustained(null, second, "b", null);
            Assert.Equal(0.2, SpiralOverlay.DeeperOpacity);
            Assert.Equal(0.2, PinkFilterOverlay.DeeperOpacity);
            SpiralOverlay.SetSustained(null, first, "a", null);
            PinkFilterOverlay.SetSustained(null, first, "a", null);
            Assert.Null(SpiralOverlay.DeeperOpacity);
            Assert.Null(PinkFilterOverlay.DeeperOpacity);
            Assert.Equal(spiralVoice, SpiralOverlay.VoiceOpacity);
            Assert.Equal(pinkVoice, PinkFilterOverlay.VoiceOpacity);
            SpiralOverlay.SetSustained(null, first, "a", 0.3);
            PinkFilterOverlay.SetSustained(null, first, "a", 0.3);
            SpiralOverlay.Stop(); PinkFilterOverlay.Stop();
            Assert.Null(SpiralOverlay.DeeperOpacity);
            Assert.Null(PinkFilterOverlay.DeeperOpacity);
        }
        finally { SpiralOverlay.Stop(); PinkFilterOverlay.Stop(); host.Close(); }
    });

    [Fact]
    public void DispatcherRampAndCleanupReleaseOnlyItsOwnBand() => AvaloniaTestDispatcher.Run(() =>
    {
        var host = new global::Avalonia.Controls.Window();
        var first = new BrowserEnhancementDispatcher { OverlayHost = host };
        var second = new BrowserEnhancementDispatcher { OverlayHost = host };
        var effect = new TriggerEffectAction { EffectType = EffectTypes.Overlay, OverlayKind = OverlayKinds.Spiral,
            EffectId = "band", Phase = EffectPhase.Start, Opacity = 0.7 };
        try
        {
            first.Overlay(effect, CancellationToken.None);
            effect.Opacity = 0.4; second.Overlay(effect, CancellationToken.None);
            effect.Phase = EffectPhase.Update; effect.Opacity = 0.2;
            first.Overlay(effect, CancellationToken.None);
            Assert.Equal(0.4, SpiralOverlay.DeeperOpacity);
            second.ResetOverlayBands();
            Assert.Equal(0.2, SpiralOverlay.DeeperOpacity);
            first.ResetOverlayBands();
            Assert.Null(SpiralOverlay.DeeperOpacity);
            effect.Phase = EffectPhase.Start; first.Overlay(effect, CancellationToken.None);
            Assert.Null(SpiralOverlay.DeeperOpacity);
        }
        finally { first.ResetOverlayBands(); second.ResetOverlayBands(); SpiralOverlay.Stop(); host.Close(); }
    });

    private sealed class Camera : IEnhancementWebcam
    {
        public event Action? OnBlink, OnMouthOpen, OnFaceLost, OnFaceFound;
        public event Action<double, double>? OnGazeMove;
        public void Blink() => OnBlink?.Invoke();
    }

    [Fact]
    public Task PlayerFeedsWebcamAndEventLog() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = EnhancementHostService.WebcamProvider;
        var camera = new Camera();
        EnhancementHostService.WebcamProvider = () => camera;
        using var media = new Media();
        var actions = new Actions();
        var player = new EnhancementPlayerWindow(null, null) { PlayerDispatcherFactory = () => actions };
        try
        {
            player.Show(); player.OpenLocalMediaFile(media.Path);
            for (int i = 0; i < 500 && !media.Audio.Playing; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.True(media.Audio.Playing);
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Video, MediaSource = "*", Rules = {
                new EnhancementRule { Trigger = new BlinkDetectedTrigger(), Action = new ConditioningControlPanel.Models.Deeper.PlayAudioAction { Path = "test" } }
            } }, "test");
            player.GetType().GetField("_isVideoMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(player, false);
            Tick(player); camera.Blink(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, actions.Calls);
            var entries = (ObservableCollection<EventLogEntry>)player.GetType().GetField("_logEntries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(player)!;
            Assert.Contains(entries, e => e.Category == EventLogEntry.EventLogCategory.Action);
            Assert.Contains(entries, e => e.Category == EventLogEntry.EventLogCategory.Engine && e.Description.Contains("blink"));
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, "replacement");
            camera.Blink(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, actions.Calls);
        }
        finally { player.Close(); EnhancementHostService.WebcamProvider = old; }
    });

    [Fact]
    public Task ReplacementRetiresQueuedAndDisplayedSpeakCards() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldDraw = SubliminalWhisperShow.DrawNow;
        using var media = new Media(); var audio = media.Audio;
        var player = new EnhancementPlayerWindow(null, null);
        var cards = new System.Collections.Generic.List<SubliminalOverlayWindow>();
        SubliminalWhisperShow.DrawNow = text => { SubliminalOverlay.CloseAll(); var card = new SubliminalOverlayWindow(text); cards.Add(card); SubliminalOverlay.Present(card); };
        // Suppress repeated cues: a busy dispatcher can otherwise
        // fire the real 250 ms timer before the first card-count assertion.
        player.SpeakSessionFactory = Runtime(async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return PhraseResult.NotAvailable; },
            startCueTimer: (_, _) => () => { }).CreateSession;
        try
        {
            player.Show(); player.OpenLocalMediaFile(media.Path);
            // The trunk player starts audio only after its waveform load (#4751), off the UI thread.
            for (int i = 0; i < 500 && !audio.Playing; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.True(audio.Playing);
            // This test drives playback with Tick. Do not let the real 100 ms UI timer
            // enter the replacement band at the old position while the queue is drained.
            var uiTimer = (DispatcherTimer)typeof(EnhancementPlayerWindow).GetField("_uiTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(player)!;
            uiTimer.Stop();
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*", TimelineItems = {
                // Keep periodic ticks outside this test's card-count assertions. The separate
                // replacement timer test below exercises recurring cues and their cancellation.
                new TimelineItem { Kind = TimelineItemKind.Effect, Start = 1, Duration = 3, EffectType = EffectTypes.Speak,
                    EffectActivation = EffectActivation.Region, EffectSpeakTarget = "yes", EffectSpeakCueMode = SpeakCueMode.Intermittent,
                    EffectSpeakCueIntervalMs = 60_000 } } };
            player.LoadEnhancementFromMemory(enhancement, "test"); Tick(player); audio.PositionSeconds = 1.2; Tick(player);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(cards); Assert.True(cards[0].IsVisible);
            SubliminalWhisperShow.DrawSpeak("queued feedback");
            player.LoadEnhancementFromMemory(enhancement, "replacement");
            Assert.False(cards[0].IsVisible);
            await Task.Delay(250); // longer than the player timer, even on a slow runner
            Dispatcher.UIThread.RunJobs();
            Assert.Single(cards);
            audio.PositionSeconds = 0; Tick(player); audio.PositionSeconds = 1.2; Tick(player);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, cards.Count);
            SubliminalWhisperShow.Draw("ambient"); Dispatcher.UIThread.RunJobs();
            Assert.True(cards[2].IsVisible);
            player.LoadEnhancementFromMemory(enhancement, "replacement again");
            Assert.True(cards[2].IsVisible);
        }
        finally { player.Close(); SubliminalOverlay.CloseAll(); SubliminalWhisperShow.DrawNow = oldDraw; }
    });

    [Fact]
    public Task ReplacementStopsRecurringSpeakCues() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var media = new Media();
        var audio = media.Audio;
        var player = new EnhancementPlayerWindow(null, null);
        var repeated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int flashes = 0;
        player.SpeakSessionFactory = Runtime(async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return PhraseResult.NotAvailable; },
            flash: _ => { if (++flashes >= 2) repeated.TrySetResult(); }).CreateSession;
        try
        {
            player.Show(); player.OpenLocalMediaFile(media.Path);
            for (int i = 0; i < 500 && !audio.Playing; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.True(audio.Playing);
            // Drive playback ticks explicitly so a later automatic band-exit tick cannot mask a
            // replacement that failed to stop the old cue timer.
            var uiTimer = (DispatcherTimer)typeof(EnhancementPlayerWindow).GetField("_uiTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(player)!;
            uiTimer.Stop();
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*", TimelineItems = {
                new TimelineItem { Kind = TimelineItemKind.Effect, Start = 1, Duration = 3, EffectType = EffectTypes.Speak,
                    EffectActivation = EffectActivation.Region, EffectSpeakTarget = "yes", EffectSpeakCueMode = SpeakCueMode.Intermittent,
                    EffectSpeakCueIntervalMs = 80 } } };
            player.LoadEnhancementFromMemory(enhancement, "test"); Tick(player); audio.PositionSeconds = 1.2; Tick(player);
            await repeated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Do not tick the old enhancement at zero: replacement itself must stop its timer.
            audio.PositionSeconds = 0;
            player.LoadEnhancementFromMemory(enhancement, "replacement");
            int atReplacement = flashes;
            await Task.Delay(350);
            Assert.Equal(atReplacement, flashes);
        }
        finally { player.Close(); }
    });

    [Fact]
    public Task PanicStopsIntermittentCuesBeforeRecognitionTeardown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        int flashes = 0;
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = Runtime(async (_, _, _) => { listening.TrySetResult(); await release.Task; return PhraseResult.NotAvailable; },
            flash: _ => flashes++).CreateSession(new TriggerEffectAction { SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Intermittent, SpeakCueIntervalMs = 80 }, null);
        var run = session.RunAsync();
        try
        {
            await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            Assert.True(flashes >= 2);
            session.Stop();
            int atStop = flashes;
            await Task.Delay(250);
            Assert.Equal(atStop, flashes);
            Assert.False(run.IsCompleted);
        }
        finally { release.TrySetResult(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
    });

    [Theory, InlineData(false, false, false), InlineData(true, false, false), InlineData(false, true, false),
     InlineData(true, true, false), InlineData(false, false, true), InlineData(false, true, true)]
    public Task PlayerDispatchesSpeakAndStopsOnBandExitOrPanic(bool panic, bool intermittent, bool pausedSeek) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldHaptics = ConditioningControlPanel.CoreHaptics.Service;
        var oldPremium = ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
        ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => true;
        using var haptics = new HapticService(new HapticSettings { Enabled = true });
        ConditioningControlPanel.CoreHaptics.Service = haptics;
        haptics.SetLayer(HapticLayer.Manual, 0.5);
        using var media = new Media(); var audio = media.Audio;
        int shows = 0, hides = 0, listens = 0, stops = 0;
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new EnhancementPlayerWindow(null, null);
        player.SpeakSessionFactory = Runtime(async (_, _, ct) =>
        {
            listens++;
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { if (intermittent) hidden.TrySetResult(); }
            return PhraseResult.NotAvailable;
        }, () => stops++, _ => { shows++; shown.TrySetResult(); }, () => { hides++; hidden.TrySetResult(); },
            _ => { shows++; shown.TrySetResult(); }).CreateSession;
        try
        {
            player.Show(); player.OpenLocalMediaFile(media.Path);
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*",
                TimelineItems = { new TimelineItem { Kind = TimelineItemKind.Effect, Start = 0.5,
                    EffectType = EffectTypes.Haptic, EffectPatternName = "pulse", EffectActivation = EffectActivation.Duration },
                  new TimelineItem { Kind = TimelineItemKind.Effect, Start = 1, Duration = 3,
                    EffectType = EffectTypes.Speak, EffectActivation = EffectActivation.Region,
                    EffectSpeakTarget = "yes", EffectSpeakCueMode = intermittent ? SpeakCueMode.Intermittent : SpeakCueMode.Persistent,
                    EffectSpeakHoldMode = panic ? SpeakHoldMode.Pause : SpeakHoldMode.KeepPlaying } }
            }, "test");
            Tick(player); audio.PositionSeconds = 1.2; Tick(player);
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(shows >= 1);
            Assert.Equal(1, listens);
            if (panic)
            {
                audio.PositionSeconds = 3.8; Tick(player);
                await Task.Delay(300);
                Assert.False(audio.Playing);
                EnhancementPlayerWindow.PanicPauseAll();
            }
            else
            {
                if (pausedSeek) Call(player, "BtnPlayPause_Click");
                if (pausedSeek && !intermittent)
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var canvas = (global::Avalonia.Controls.Canvas)player.GetType().GetField("_miniTimelineCanvas", flags)!.GetValue(player)!;
                    canvas.Measure(new global::Avalonia.Size(600, 40)); canvas.Arrange(new global::Avalonia.Rect(0, 0, 600, 40));
                    player.GetType().GetField("_miniTotalSeconds", flags)!.SetValue(player, 20d);
                    Call(player, "SeekToMiniPosition", canvas.Bounds.Width / 4);
                    Assert.Equal(1, stops);
                }
                else { audio.PositionSeconds = 5; Tick(player); }
                if (pausedSeek) Assert.False(audio.Playing);
            }
            await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (intermittent) { await Task.Delay(300); var last = shows; await Task.Delay(300); Assert.Equal(last, shows); }
            else Assert.Equal(1, hides);
            if (panic) { Assert.False(audio.Playing); var before = shows; Tick(player); await Task.Delay(20); Assert.Equal(before, shows); }
            if (!audio.Playing) Call(player, "BtnPlayPause_Click");
            audio.PositionSeconds = 0; shows = 0;
            shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.LoadEnhancementFromMemory(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*", TimelineItems = {
                new TimelineItem { Id = "band", Kind = TimelineItemKind.Rule, Start = 1, Duration = 2,
                    Trigger = new TimeReachedTrigger { Time = 1.2 }, Action = new TriggerEffectAction { EffectType = EffectTypes.Haptic, PatternName = "pulse" } },
                new TimelineItem { Kind = TimelineItemKind.Rule, Duration = double.MaxValue,
                    Trigger = panic ? new RegionExitedTrigger { RegionId = "band" } : new RegionEnteredTrigger { RegionId = "band" },
                    Action = new TriggerEffectAction { EffectType = EffectTypes.Speak, SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent, SpeakHoldMode = SpeakHoldMode.KeepPlaying } }
            } }, "region");
            Tick(player); audio.PositionSeconds = 1.2; Tick(player);
            if (panic) { Assert.Equal(0, shows); audio.PositionSeconds = 3.2; Tick(player); }
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, shows);
            player.Close();
            Assert.Equal(0.5, haptics.GetLayer(HapticLayer.Manual));
        }
        finally
        {
            player.Close();
            ConditioningControlPanel.CoreHaptics.Service = oldHaptics;
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = oldPremium;
        }
    });
}
