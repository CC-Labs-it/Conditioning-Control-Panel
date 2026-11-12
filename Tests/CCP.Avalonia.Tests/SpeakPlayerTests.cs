using System;
using System.IO;
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
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class SpeakPlayerTests
{
    private sealed class Audio : IDeeperLocalAudio
    {
        public double PositionSeconds { get; set; }
        public double DurationSeconds => 20;
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
        Action? stopListening = null, Action<string>? show = null, Action? hide = null, Action<string>? flash = null) =>
        new(() => true, () => true, () => Task.FromResult(true), () => false, () => { }, () => { }, () => false,
            listen, stopListening ?? (() => { }), show ?? (_ => { }), hide ?? (() => { }), flash);

    [Fact]
    public Task ReplacementRetiresQueuedAndDisplayedSpeakCards() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldDraw = SubliminalWhisperShow.DrawNow;
        using var media = new Media(); var audio = media.Audio;
        var player = new EnhancementPlayerWindow(null, null);
        var cards = new System.Collections.Generic.List<SubliminalOverlayWindow>();
        SubliminalWhisperShow.DrawNow = text => { SubliminalOverlay.CloseAll(); var card = new SubliminalOverlayWindow(text); cards.Add(card); SubliminalOverlay.Present(card); };
        player.SpeakSessionFactory = Runtime(async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return PhraseResult.NotAvailable; }).CreateSession;
        try
        {
            player.Show(); player.OpenLocalMediaFile(media.Path);
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*", TimelineItems = {
                new TimelineItem { Kind = TimelineItemKind.Effect, Start = 1, Duration = 3, EffectType = EffectTypes.Speak,
                    EffectActivation = EffectActivation.Region, EffectSpeakTarget = "yes", EffectSpeakCueMode = SpeakCueMode.Intermittent } } };
            player.LoadEnhancementFromMemory(enhancement, "test"); Tick(player); audio.PositionSeconds = 1.2; Tick(player);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(cards); Assert.True(cards[0].IsVisible);
            SubliminalWhisperShow.DrawSpeak("queued feedback");
            player.LoadEnhancementFromMemory(enhancement, "replacement");
            Assert.False(cards[0].IsVisible);
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
        return Task.CompletedTask;
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
