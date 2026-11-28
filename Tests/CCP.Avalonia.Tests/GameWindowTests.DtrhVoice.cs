using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF DtrhHostService.cs:329-342,360-371,386-390.
    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, true)]
    public void DtrhVoice_BarkAndSfxRespectSpeakingAndRunClearsGate(bool sound, int state, bool expected) => RunDtrh(true, (_, _, _, _) =>
    {
        var oldEngine = BarkHead.Engine;
        var oldHaptic = GameWindow.DtrhHapticEvent;
        var settings = CoreSettings.Current;
        var old = (settings.AvatarEnabled, settings.MasterVolume, settings.BarkChatSuppressionMs);
        var spoken = new List<BarkSpeech>();
        var sounds = new List<(string, float)>();
        var order = new List<string>();
        try
        {
            (settings.AvatarEnabled, settings.MasterVolume, settings.BarkChatSuppressionMs) = (true, 50, 0);
            var rule = new BarkRule { Id = "voice-test", Trigger = "ChaosBubbleDetonated", VariantPool = new() { new BarkVariant("hit {payload}") } };
            var engine = new BarkEngine(() => new BarkRuleSet(new[] { rule }))
            {
                Speak = line => { order.Add("voice"); spoken.Add(line); },
            };
            GameWindow.DtrhHapticEvent = _ => order.Add("haptic");
            engine.Start();
            BarkHead.Engine = engine;
            var w = GameWindow.Launch("dtrh")!;
            w.DtrhPlaySfx = (name, scale) => sounds.Add((name, scale));
            if (state > 0) w.HandleMessage("{\"type\":\"vn-speaking\",\"on\":true}");
            if (state == 2) w.HandleMessage("{\"type\":\"run-started\"}");
            if (sound) w.HandleMessage("{\"type\":\"sfx\",\"name\":\"wave_clear\"}");
            else w.HandleMessage("{\"type\":\"bark\",\"event\":\"detonated\",\"variant\":\"test\"}");
            if (sound && expected) Assert.Equal(("wave_clear", 0.6f), Assert.Single(sounds));
            else Assert.Empty(sounds);
            if (!sound && expected) Assert.Equal("hit test", Assert.Single(spoken).Text);
            else Assert.Empty(spoken);
            Assert.Equal(sound ? Array.Empty<string>() : expected ? new[] { "haptic", "voice" } : new[] { "haptic" }, order);
            if (!sound)
            {
                w.HandleMessage("{\"type\":\"bark\",\"event\":\"effect-fired\",\"kind\":\"flash\"}");
                Assert.Equal(expected ? new[] { "haptic", "voice", "haptic" } : new[] { "haptic", "haptic" }, order);
            }
            w.Close();
        }
        finally
        {
            BarkHead.Engine = oldEngine;
            GameWindow.DtrhHapticEvent = oldHaptic;
            (settings.AvatarEnabled, settings.MasterVolume, settings.BarkChatSuppressionMs) = old;
        }
    });

    [Theory]
    [InlineData("wave_clear", "chaos/wave_clear.mp3", 0.4f)]
    [InlineData("ripple_cast", "chaos/ripple_cast.mp3", 0.3f)]
    [InlineData("ordinary", "chaos/ordinary.mp3", 0.1f)]
    public void DtrhVoice_SoundUsesWpfCueAndScale(string name, string relative, float volume) => RunDtrh(true, (_, _, _, _) =>
    {
        var oldResolver = CoreModArt.AudioOverridePathProvider;
        var oldPlayer = CoreAudio.PlayOneShotProvider;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var path = Path.GetTempFileName();
        var played = new List<(string, float)>();
        try
        {
            CoreSettings.Current.MasterVolume = 50;
            CoreModArt.AudioOverridePathProvider = rel => rel == relative ? path : null;
            CoreAudio.PlayOneShotProvider = (file, gain, _, _, _) => played.Add((file, gain));
            var w = GameWindow.Launch("dtrh")!;
            w.HandleMessage("{\"type\":\"sfx\",\"name\":\"" + name + "\",\"scale\":0.2}");
            var result = Assert.Single(played);
            Assert.Equal(path, result.Item1);
            Assert.Equal(volume, result.Item2, 3);
            w.Close();
        }
        finally
        {
            CoreModArt.AudioOverridePathProvider = oldResolver;
            CoreAudio.PlayOneShotProvider = oldPlayer;
            CoreSettings.Current.MasterVolume = oldVolume;
            File.Delete(path);
        }
    });

    [Fact]
    public void DtrhVoice_ClosedWindowIgnoresLateSounds() => RunDtrh(true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        var sounds = new List<string>();
        w.DtrhPlaySfx = (name, _) => sounds.Add(name);
        w.Close();
        w.HandleMessage("{\"type\":\"sfx\",\"name\":\"wave_clear\"}");
        Assert.Empty(sounds);
    });

}
