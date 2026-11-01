using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubePresetSpeechParityTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // WPF Speech.cs:503-547,565-586,2936-3033: the reading clock starts after the slow typewriter.
    [Fact]
    public Task PresetTypesGraduallyAndKeepsItsReadingTime() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.AvatarMuted = true;
        s.BubbleDurationSeconds = 2;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.Giggle("1234567890");
            Assert.Equal("", text.Text);
            Assert.Equal(TimeSpan.FromMilliseconds(75), tube.TypewriterInterval);
            Assert.Equal(TimeSpan.FromSeconds(2.75), tube.SpeechBubbleInterval);
            var stale = tube.TypewriterFrame!;
            stale();
            Assert.Equal("1", text.Text);
            tube.GigglePriority("replacement", playSound: false, aiGenerated: false);
            stale();
            Assert.Equal("replacement", text.Text); // an interrupted tick cannot overwrite a newer line

            tube.EndSpeechBubble();
            tube.SpeechDelayScheduler = (_, fire) => { fire(); return () => { }; };
            tube.Giggle("1234567890");
            while (tube.TypewriterFrame is { } frame) frame();
            Assert.Equal("1234567890", text.Text);
            Assert.Null(tube.TypewriterFrame);

            tube.EndSpeechBubble();
            tube.Giggle(new string('a', 200));
            Assert.Equal(TimeSpan.FromMilliseconds(35), tube.TypewriterInterval);
            Assert.Equal(TimeSpan.FromSeconds(5.5), tube.SpeechBubbleInterval);
            tube.TypewriterFrame!();
            Assert.Equal("aa", text.Text); // two characters per tick at length 200
            while (tube.TypewriterFrame is { } mediumFrame) mediumFrame();
            Assert.Equal(new string('a', 200), text.Text);

            tube.EndSpeechBubble();
            tube.Giggle(new string('b', 400));
            Assert.Equal(TimeSpan.FromMilliseconds(24), tube.TypewriterInterval);
            Assert.Equal(TimeSpan.FromSeconds(4.4), tube.SpeechBubbleInterval);
            tube.TypewriterFrame!();
            Assert.Equal("bbbb", text.Text);
            while (tube.TypewriterFrame is { } longFrame) longFrame();
            Assert.Equal(new string('b', 400), text.Text);
        }
        finally { tube.Close(); }
        return Task.CompletedTask;
    });

    // WPF Speech.cs:531-539 and Reactions.cs:322-347: 4/5 presets choose giggle1-4 at half volume.
    [Fact]
    public Task PresetFallbackUsesTheQuietModAwareCue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.AvatarMuted = false;
        s.MasterVolume = 64;
        var oldMod = CoreMods.ActiveModIdProvider;
        var oldPath = CoreModArt.AudioOverridePathProvider;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var path = Path.GetTempFileName();
        var names = new List<string>();
        var played = new List<(string Path, float Volume, string Tag)>();
        CoreMods.ActiveModIdProvider = () => "test";
        CoreModArt.AudioOverridePathProvider = name => { names.Add(name); return path; };
        CoreAudio.PlayOneShotProvider = (p, v, tag, _, _) => played.Add((p, v, tag));
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.Giggle("fallback");
            var cue = Assert.Single(played);
            Assert.Contains(Assert.Single(names), new[] { "giggle1.MP3", "giggle2.MP3", "giggle3.MP3", "giggle4.MP3" });
            Assert.Equal(path, cue.Path);
            Assert.Equal("bubble-fallback", cue.Tag);
            Assert.Equal(0.256, cue.Volume, 3);
            tube.GigglePriority("silent priority", playSound: false, aiGenerated: false);
            Assert.Single(played); // WPF suppresses this fallback for the AI-source path
        }
        finally
        {
            tube.Close();
            CoreMods.ActiveModIdProvider = oldMod;
            CoreModArt.AudioOverridePathProvider = oldPath;
            CoreAudio.PlayOneShotProvider = oldAudio;
            File.Delete(path);
        }
        return Task.CompletedTask;
    });
}
