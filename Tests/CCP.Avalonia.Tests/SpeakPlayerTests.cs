using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Avalonia.Tests;

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task PlayerDispatchesSpeakAndStopsOnBandExitOrPanic(bool panic, bool intermittent) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = DeeperLocalAudio.Open;
        var oldHaptics = ConditioningControlPanel.CoreHaptics.Service;
        var oldPremium = ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
        ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => true;
        using var haptics = new HapticService(new HapticSettings { Enabled = true });
        ConditioningControlPanel.CoreHaptics.Service = haptics;
        haptics.SetLayer(HapticLayer.Manual, 0.5);
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var media = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        File.WriteAllBytes(media, new byte[16]);
        int shows = 0, hides = 0, listens = 0;
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new EnhancementPlayerWindow(null, null);
        var runtime = new SpeakPromptRuntime(
            () => true, () => true, () => Task.FromResult(true), () => false, () => { }, () => { }, () => false,
            async (_, _, ct) =>
            {
                listens++;
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { if (intermittent) hidden.TrySetResult(); }
                return PhraseResult.NotAvailable;
            }, () => { },
            _ => { shows++; shown.TrySetResult(); }, () => { hides++; hidden.TrySetResult(); },
            _ => { shows++; shown.TrySetResult(); });
        player.SpeakSessionFactory = runtime.CreateSession;
        try
        {
            player.Show();
            player.OpenLocalMediaFile(media);
            player.LoadEnhancementFromMemory(new Enhancement
            {
                MediaType = MediaTypes.Audio, MediaSource = "*",
                TimelineItems = { new TimelineItem { Kind = TimelineItemKind.Effect, Start = 0.5,
                    EffectType = EffectTypes.Haptic, EffectPatternName = "pulse", EffectActivation = EffectActivation.Duration },
                  new TimelineItem { Kind = TimelineItemKind.Effect, Start = 1, Duration = 3,
                    EffectType = EffectTypes.Speak, EffectActivation = EffectActivation.Region,
                    EffectSpeakTarget = "yes", EffectSpeakCueMode = intermittent ? SpeakCueMode.Intermittent : SpeakCueMode.Persistent,
                    EffectSpeakHoldMode = panic ? SpeakHoldMode.Pause : SpeakHoldMode.KeepPlaying } }
            }, "test");
            void Tick() => player.GetType().GetMethod("UiTimer_Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(player, new object?[] { null, EventArgs.Empty });
            Tick();
            audio.PositionSeconds = 1.2;
            Tick();
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(shows >= 1);
            Assert.Equal(1, listens);
            if (panic)
            {
                audio.PositionSeconds = 3.8;
                Tick();
                await Task.Delay(300);
                Assert.False(audio.Playing);
                EnhancementPlayerWindow.PanicPauseAll();
            }
            else { audio.PositionSeconds = 5; Tick(); }
            await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (intermittent)
            {
                await Task.Delay(300);
                var last = shows;
                await Task.Delay(300);
                Assert.Equal(last, shows);
            }
            else Assert.Equal(1, hides);
            if (panic) Assert.False(audio.Playing);
            player.Close();
            Assert.Equal(0.5, haptics.GetLayer(HapticLayer.Manual));
        }
        finally
        {
            player.Close();
            DeeperLocalAudio.Open = old;
            ConditioningControlPanel.CoreHaptics.Service = oldHaptics;
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = oldPremium;
            File.Delete(media);
        }
    });
}
