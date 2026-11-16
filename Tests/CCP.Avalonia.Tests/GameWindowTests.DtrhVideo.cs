using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private sealed class DtrhDecoder : IMandatoryPlayback
    {
        public bool Paused;
        public long Time { get; set; }
        public long Length => 1000;
        public bool IsPlaying => true;
        public bool IsSeekable => false;
        public bool HasVideo => true;
        public void SetPause(bool paused) => Paused = paused;
        public void Stop() { }
        public void Dispose() { }
    }

    [Fact]
    public void Dtrh_video_freeze_and_mute_are_released() => RunDtrh(true, (_, _, _, _) =>
    {
        var overlay = MandatoryVideoOverlay.Instance;
        var field = typeof(MandatoryVideoOverlay).GetField("_playback", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var old = field.GetValue(overlay);
        var closing = typeof(MandatoryVideoOverlay).GetField("_closing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldClosing = closing.GetValue(overlay);
        var paused = typeof(MandatoryVideoOverlay).GetProperty("PrimaryPaused", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldPaused = paused.GetValue(overlay);
        paused.SetValue(overlay, false);
        closing.SetValue(overlay, false);
        var w = GameWindow.Launch("dtrh")!;
        var decoder = new DtrhDecoder();
        closing.SetValue(overlay, false);
        paused.SetValue(overlay, false);
        field.SetValue(overlay, decoder);
        try
        {
            Assert.Same(decoder, field.GetValue(overlay));
            Assert.False((bool)closing.GetValue(overlay)!);
            Assert.False(overlay.PrimaryPaused);
            Assert.True(w.IsVisible);
            Assert.False((bool)typeof(GameWindow).GetField("_closing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!);
            // WPF boot.js sends the saved mute once; DtrhHostService.cs:364-385 never releases it on run start or end.
            w.HandleMessage("{\"type\":\"mute-state\",\"on\":true}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            Assert.True(overlay.ExternalMuted);
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            Assert.True((bool)typeof(GameWindow).GetField("_dtrhFrozen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!);
            Assert.True(decoder.Paused);
            w.HandleMessage("{\"type\":\"run-ended\"}");
            Assert.False(decoder.Paused);
            Assert.True(overlay.ExternalMuted);
            Assert.Equal(0, overlay.EffectivePlayerVolume);
            overlay.SetExternalMute(false);
            overlay.SetExternalMute(true); // flag and overlay can disagree on close
            w.Close();
            Assert.False(overlay.ExternalMuted);
        }
        finally { w.Close(); overlay.SetExternalMute(false); field.SetValue(overlay, old); closing.SetValue(overlay, oldClosing); paused.SetValue(overlay, oldPaused); }
    });

    [Fact]
    public void Dtrh_only_fires_clamped_audio_payload() => RunDtrh(true, (_, _, _, _) =>
    {
        var old = EffectPayload.Firer;
        var seen = new List<EffectPayload>();
        EffectPayload.Firer = seen.Add;
        var w = GameWindow.Launch("dtrh")!;
        try
        {
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"audio\",\"strength\":150,\"durationMult\":20}");
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"video\"}");
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"flash\"}");
            var payload = Assert.Single(seen);
            Assert.Equal(EffectBubblePayloadKind.Audio, payload.Kind);
            Assert.Equal(100, payload.Strength);
            Assert.Equal(10, payload.DurationMult);
            seen.Clear();
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"audio\",\"strength\":-2,\"durationMult\":0}");
            Assert.Equal(0, Assert.Single(seen).Strength);
            Assert.Equal(0.1, seen[0].DurationMult);
        }
        finally { w.Close(); EffectPayload.Firer = old; }
    });

    // Stricter than WPF DtrhHostService.cs:343-348: a page settling after panic cannot fire a whisper or freeze.
    [Fact]
    public void Dtrh_panic_settle_window_fires_no_payload_or_freeze() => RunDtrh(true, (_, _, _, _) =>
    {
        var old = EffectPayload.Firer;
        var seen = new List<EffectPayload>();
        EffectPayload.Firer = seen.Add;
        var w = GameWindow.Launch("dtrh")!;
        try
        {
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            GameWindow.CloseAllForPanic();
            Assert.True(w.IsVisible); // still settling
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"audio\"}");
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            Assert.Empty(seen);
            Assert.False((bool)typeof(GameWindow).GetField("_dtrhFrozen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!);
        }
        finally { w.Close(); EffectPayload.Firer = old; }
    });

    // WPF EffectPayloadFiring.cs:163-168: through the production runner, the audio payload is a whisper.
    [Fact]
    public void Dtrh_audio_payload_whispers_through_the_production_runner() => RunDtrh(true, (_, _, _, _) =>
    {
        var (oldFirer, oldShow, oldPool) = (EffectPayload.Firer, CoreSubliminal.ShowProvider, CoreSettings.Current.SubliminalPool);
        var shown = new List<string>();
        EffectPayload.Firer = global::ConditioningControlPanel.Avalonia.App.FireChaosPayload;
        CoreSubliminal.ShowProvider = shown.Add;
        CoreSettings.Current.SubliminalPool = new() { ["dtrh whisper"] = true };
        var w = GameWindow.Launch("dtrh")!;
        try
        {
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"audio\"}");
            Assert.Equal(new[] { "dtrh whisper" }, shown);
        }
        finally { w.Close(); (EffectPayload.Firer, CoreSubliminal.ShowProvider, CoreSettings.Current.SubliminalPool) = (oldFirer, oldShow, oldPool); }
    });

    // WPF DtrhHostService.cs:814-837 + :607: a freeze pauses the companion's line; run end resumes it.
    [Fact]
    public void Dtrh_freeze_pauses_and_releases_the_spoken_line() => RunDtrh(true, (shell, _, _, _) =>
    {
        var tube = new AvatarTubeWindow(shell);
        AvatarTubeWindow.TestLive = tube;
        var pauses = new List<bool>();
        Action stop = () => { };
        LibVlcAudio.Pausers.AddOrUpdate(stop, pauses.Add);
        typeof(AvatarTubeWindow).GetField("_stopSpoken", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tube, stop);
        var w = GameWindow.Launch("dtrh")!;
        try
        {
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            Assert.Equal(new[] { true }, pauses);
            w.HandleMessage("{\"type\":\"run-ended\"}");
            Assert.Equal(new[] { true, false }, pauses);
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            w.Close();   // teardown releases a live freeze too
            Assert.Equal(new[] { true, false, true, false }, pauses);
        }
        finally { w.Close(); AvatarTubeWindow.TestLive = null; tube.Close(); }
    });

    // WPF DtrhHostService.cs:819-835,1073: both run end and close release a rapid freeze.
    [Fact]
    public void Dtrh_rapid_freeze_release_cannot_leave_the_voice_paused() => RunDtrh(true, (shell, _, _, _) =>
    {
        var oldTube = AvatarTubeWindow.TestLive;
        var tube = new AvatarTubeWindow(shell);
        AvatarTubeWindow.TestLive = tube;
        var queued = new List<Action>();
        var applied = new List<bool>();
        var control = LibVlcAudio.LatestPauseControl(new object(), () => false, applied.Add, queued.Add);
        Action stop = () => { };
        LibVlcAudio.Pausers.AddOrUpdate(stop, control.Set);
        typeof(AvatarTubeWindow).GetField("_stopSpoken", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tube, stop);
        var w = GameWindow.Launch("dtrh")!;
        try
        {
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            w.HandleMessage("{\"type\":\"run-ended\"}");
            Assert.Equal(2, queued.Count);
            queued[1](); queued[0]();
            Assert.Equal(new[] { false, false }, applied);
            queued.Clear(); applied.Clear();
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            w.Close();
            Assert.Equal(2, queued.Count);
            queued[1](); queued[0]();
            Assert.Equal(new[] { false, false }, applied);
        }
        finally
        {
            w.Close(); LibVlcAudio.Pausers.Remove(stop); AvatarTubeWindow.TestLive = oldTube; tube.Close();
        }
    });

    [Fact]
    public void Dtrh_video_scheduler_posts_covering_state() => RunDtrh(true, (_, _, _, _) =>
    {
        var scheduler = new ConditioningControlPanel.Services.MandatoryVideoScheduler(MandatoryVideoOverlay.Instance);
        var old = CoreEngine.Video;
        CoreEngine.Video = scheduler;
        var w = GameWindow.Launch("dtrh")!;
        Dispatcher.UIThread.RunJobs();
        var messages = new List<JObject>();
        w.Posted += json => messages.Add(JObject.Parse(json));
        try
        {
            foreach (var name in new[] { "VideoStarted", "VideoEnded" })
            {
                var field = scheduler.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
                ((Action?)field.GetValue(scheduler))?.Invoke();
            }
            Dispatcher.UIThread.RunJobs();
            var states = messages.FindAll(m => (string?)m["type"] == "payload-state");
            Assert.Equal(2, states.Count);
            Assert.Equal("video", (string?)states[0]["kind"]);
            Assert.True((bool)states[0]["on"]!);
            Assert.False((bool)states[1]["on"]!);
        }
        finally { w.Close(); CoreEngine.Video = old; }
    });
}
