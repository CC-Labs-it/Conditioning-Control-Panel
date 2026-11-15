using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Threading;
using ConditioningControlPanel;
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
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            Assert.True((bool)typeof(GameWindow).GetField("_dtrhFrozen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!);
            Assert.True(decoder.Paused);
            w.HandleMessage("{\"type\":\"run-ended\"}");
            Assert.False(decoder.Paused);
            w.HandleMessage("{\"type\":\"mute-state\",\"on\":true}");
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
