using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Rain's tube clips on WPF (7.2, lane R0 of handoff-1030/RAIN-V2-PLAN.md): twelve animated WebP clips
/// embedded under Resources/rain_emotes, registered as the "v2" generation (never an avatar set), and
/// played once by the SkiaSharp lane (EmoteClipPlayer) on the same two layers XamlAnimatedGif uses.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RainTubeClipsTests
{
    private static readonly string[] RainMods = { BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId, BuiltInMods.CCPDefaultId };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string RainDir => Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "rain_emotes");

    private static Uri Pack(string clip) => new($"pack://application:,,,/Resources/rain_emotes/{clip}.webp", UriKind.Absolute);

    private static HashSet<string> MappedClips()
    {
        var map = JObject.Parse(File.ReadAllText(Path.Combine(RainDir, "emotes.json")));
        var clips = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in (JArray)map["idleRotation"]!) clips.Add((string)it["clip"]!);
        foreach (var c in (JArray)map["talking"]!["pool"]!) clips.Add((string)c!);
        foreach (var c in (JArray)map["expressive"]!["pool"]!) clips.Add((string)c!);
        foreach (var c in (JArray)map["clickEmotes"]!) clips.Add((string)c!);
        foreach (var p in ((JObject)map["stemPrefix"]!).Properties()) clips.Add((string)p.Value!);
        foreach (var p in ((JObject)map["mood"]!).Properties()) clips.Add((string)p.Value!);
        return clips;
    }

    [Fact]
    public void RainIsTheV2Generation_NeverAnAvatarSet()
    {
        foreach (var mod in RainMods)
        {
            Assert.Equal("rain_emotes", AvatarTubeWindow.EmoteGenerationFolder(mod, "v2"));
            // Not a set: no arrows, no level picker, no set-2 preview.
            Assert.Null(AvatarTubeWindow.EmoteIdleClipUri(mod, 2, installedPath: null));
            Assert.NotNull(AvatarTubeWindow.EmoteIdleClipUri(mod, 1, installedPath: null));
        }
        Assert.Null(AvatarTubeWindow.EmoteGenerationFolder(BuiltInMods.LockedId, "v2"));
        Assert.Null(AvatarTubeWindow.EmoteGenerationFolder(BuiltInMods.BambiSleepId, "v1"));
    }

    [Fact]
    public void EveryMappedClip_ShipsAsEmbeddedWebp()
    {
        Assert.Equal(12, Directory.GetFiles(RainDir, "*.webp").Length);
        Assert.Empty(Directory.GetFiles(RainDir, "*.gif"));
        foreach (var clip in MappedClips())
        {
            var sri = Application.GetResourceStream(Pack(clip));
            Assert.True(sri != null, clip + ".webp not embedded");
            sri!.Stream.Dispose();
        }
        var map = JObject.Parse(File.ReadAllText(Path.Combine(RainDir, "emotes.json")));
        var timing = JObject.Parse(File.ReadAllText(Path.Combine(RainDir, "clip_timing.json")));
        foreach (var c in (JArray)map["talking"]!["pool"]!) Assert.NotNull(timing[(string)c!]);
    }

    [Fact]
    public void EveryClip_DecodesAsSixSecondsOfRealAlpha()
    {
        foreach (var file in Directory.GetFiles(RainDir, "*.webp"))
        {
            using var d = ConditioningControlPanel.EmoteClipPlayer.Decode(File.ReadAllBytes(file));
            Assert.NotNull(d);
            Assert.Equal(192, d!.Canvas.Width);
            Assert.Equal(309, d.Canvas.Height);
            Assert.InRange(d.Durations.Length, 85, 95);
            Assert.InRange(d.Durations.Sum(), 5800, 6200);
            bool clear = false, solid = false;
            for (int y = 0; y < d.Canvas.Height && !(clear && solid); y += 3)
                for (int x = 0; x < d.Canvas.Width; x += 3)
                {
                    var a = d.Canvas.GetPixel(x, y).Alpha;
                    clear |= a == 0; solid |= a == 255;
                }
            Assert.True(clear && solid, Path.GetFileName(file) + " has no real alpha");
        }
    }

    [Fact]
    public void RainClip_PlaysThroughTheWebpLane()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var img = new Image();
            var player = new ConditioningControlPanel.EmoteClipPlayer(img) { ManualClock = true };
            long now = 0;
            player.Now = () => now;
            player.LoadDecoded(Pack("greeting"), ConditioningControlPanel.EmoteClipPlayer.Decode(ConditioningControlPanel.EmoteClipPlayer.ReadBytes(Pack("greeting")))!);
            Assert.IsAssignableFrom<WriteableBitmap>(img.Source);
            now = 6500; player.Tick(now);
            Assert.True(player.IsComplete);
            Assert.True(AvatarTubeWindow.IsWebpClip(player.Source));
        });

    [Fact]
    public void Attribution_ShipsBesideTheClips()
    {
        var text = File.ReadAllText(Path.Combine(RainDir, "ATTRIBUTION.txt"));
        Assert.Contains("creativecommons.org/licenses/by/4.0", text);
        Assert.Contains("Blender Foundation", text);
    }
}
