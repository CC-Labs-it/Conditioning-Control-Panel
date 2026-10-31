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
        return SourceRoots.RepoRoot;
    }

    private static string RainDir => Path.Combine(RepoRoot(), "Assets", "rain_emotes");

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
        Assert.Equal(17, Directory.GetFiles(RainDir, "*.webp").Length);
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

    private static readonly string[] Reactions = { "joy", "surprise", "affection", "confidence", "disappointment" };

    [Fact]
    public void EveryMappedClip_IsAFileAndInTheCsproj()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "ConditioningControlPanel.csproj"));
        var includes = System.Text.RegularExpressions.Regex.Matches(csproj, "<Resource Include=\"\\.\\.\\\\Assets\\\\rain_emotes\\\\([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(includes);
        foreach (var clip in MappedClips().Concat(Reactions))
        {
            var name = clip + ".webp";
            Assert.True(File.Exists(Path.Combine(RainDir, name)), name + " missing on disk");
            Assert.True(includes.Any(inc => inc == name || inc == "*.webp"), name + " not a Resource in the csproj");
        }
    }

    [Fact]
    public void TheFiveReactions_AreMappedByThePlanTable()
    {
        var map = JObject.Parse(File.ReadAllText(Path.Combine(RainDir, "emotes.json")));
        var stem = (JObject)map["stemPrefix"]!;
        var mood = (JObject)map["mood"]!;
        foreach (var s in new[] { "level_up", "achievement" }) Assert.Equal("joy", (string)stem[s]!);
        Assert.Equal("joy", (string)mood["triumphant"]!);
        foreach (var s in new[] { "video_check_pass", "quest_done", "lockdown_on", "kw" }) Assert.Equal("confidence", (string)stem[s]!);
        foreach (var m in new[] { "proud", "intense" }) Assert.Equal("confidence", (string)mood[m]!);
        foreach (var s in new[] { "face_lost", "attention_lost", "video_check_fail" }) Assert.Equal("disappointment", (string)stem[s]!);
        foreach (var s in new[] { "session_complete", "video_end", "gaze_reward", "hold_success" }) Assert.Equal("affection", (string)stem[s]!);
        foreach (var m in new[] { "soft", "warm", "tender", "adoring" }) Assert.Equal("affection", (string)mood[m]!);
        Assert.Equal("surprise", (string)stem["remote"]!);
        Assert.Equal("surprise", (string)mood["responsive"]!);
        // No drift clip yet: trance moments stay on idle. The rest keeps its 2026-10-24 pick.
        foreach (var s in new[] { "video_start", "pinkrush", "braindrain", "mindwipe" }) Assert.Equal("idle", (string)stem[s]!);
        Assert.Equal("idle", (string)mood["hypnotic"]!);
        Assert.Equal("giggle", (string)stem["bubble_pop"]!);
        Assert.Equal("smirk", (string)stem["session_stopped"]!);
        Assert.Equal("shy_hold", (string)stem["cam_denied"]!);
        Assert.Equal("greeting", (string)stem["session_start"]!);

        var timing = JObject.Parse(File.ReadAllText(Path.Combine(RainDir, "clip_timing.json")));
        foreach (var r in Reactions) Assert.Equal(6000, (int)timing[r]!["durationMs"]!);
    }

    [Fact]
    public void EachReaction_DecodesWithSkCodec_ManyFramesAndAlphaKept()
    {
        foreach (var r in Reactions)
        {
            using var data = SkiaSharp.SKData.CreateCopy(File.ReadAllBytes(Path.Combine(RainDir, r + ".webp")));
            using var codec = SkiaSharp.SKCodec.Create(data);
            Assert.NotNull(codec);
            Assert.Equal(SkiaSharp.SKEncodedImageFormat.Webp, codec.EncodedFormat);
            Assert.True(codec.FrameCount > 1, r + " is a still");
            Assert.NotEqual(SkiaSharp.SKAlphaType.Opaque, codec.Info.AlphaType);
            var info = new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
            using var bmp = new SkiaSharp.SKBitmap(info);
            // A middle frame (the codec rebuilds whatever it builds on): transparency must survive past frame 0.
            bmp.Erase(SkiaSharp.SKColors.Transparent);
            var res = codec.GetPixels(info, bmp.GetPixels(), new SkiaSharp.SKCodecOptions(codec.FrameCount / 2, -1));
            Assert.True(res == SkiaSharp.SKCodecResult.Success || res == SkiaSharp.SKCodecResult.IncompleteInput, r + ": " + res);
            bool clear = false, solid = false;
            for (int y = 0; y < info.Height && !(clear && solid); y += 3)
                for (int x = 0; x < info.Width; x += 3)
                {
                    var a = bmp.GetPixel(x, y).Alpha;
                    clear |= a == 0; solid |= a == 255;
                }
            Assert.True(clear && solid, r + " lost its alpha mid clip");
        }
    }

    [Fact]
    public void Attribution_ShipsBesideTheClips()
    {
        var text = File.ReadAllText(Path.Combine(RainDir, "ATTRIBUTION.txt"));
        Assert.Contains("creativecommons.org/licenses/by/4.0", text);
        Assert.Contains("Blender Foundation", text);
    }
}
