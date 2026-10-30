using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;
public sealed class RainGenerationTests
{
    private static Task Run(Func<Task> action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
            .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        return action();
    });
    private static Task Run(Action action) => Run(() => { action(); return Task.CompletedTask; });
    private static byte[] Asset(string name)
    {
        using var s = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + (name.EndsWith(".gif") ? "avatar0_emotes/" : "rain_emotes/") + name));
        using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray();
    }
    [Fact]
    public Task PillFlipsGlobalGenerationWithoutAddingAvatarSets() => Run(() =>
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreMods.ActiveModIdProvider = () => "builtin-bambisleep";
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "builtin-bambisleep" }, null, false);
        service.Current.AvatarGeneration = "v1";
        var tube = new AvatarTubeWindow();
        try
        {
            var sets = tube.PickableAvatarSets();
            Assert.Equal(new[] { 1 }, sets);
            tube.UpdateGenerationPill();
            Assert.True(tube.FindControl<Border>("BtnAvatarGeneration")!.IsHitTestVisible);
            tube.FlipEmoteGeneration();
            Assert.Equal("v2", service.Current.AvatarGeneration);
            Assert.Equal("rain_emotes", tube.CurrentEmoteFolder);
            Assert.Equal(sets, tube.PickableAvatarSets());
            tube.EmotePortraitMode = true; tube.UpdateGenerationPill(); tube.FlipEmoteGeneration();
            Assert.False(tube.FindControl<Border>("BtnAvatarGeneration")!.IsHitTestVisible);
            Assert.Equal("v2", service.Current.AvatarGeneration);
            tube.EmotePortraitMode = false;
            tube.FindControl<Grid>("PossessionGlitchLayer")!.IsVisible = true;
            tube.UpdateGenerationPill();
            Assert.False(tube.FindControl<Border>("BtnAvatarGeneration")!.IsHitTestVisible);
            tube.FindControl<Grid>("PossessionGlitchLayer")!.IsVisible = false;
            tube.FlipEmoteGeneration(); Assert.Equal("v1", service.Current.AvatarGeneration);
            CoreMods.ActiveModIdProvider = () => "builtin-locked";
            tube.UpdateGenerationPill();
            Assert.False(tube.FindControl<Border>("BtnAvatarGeneration")!.IsHitTestVisible);
        }
        finally { tube.StopEmoteGeneration(); tube.Close(); }
    });
    [Fact]
    public Task WebpAdvancesOnceAndHoldsLastFrameAtAuthoredTiming() => Run(() =>
    {
        var timing = JObject.Parse(System.Text.Encoding.UTF8.GetString(Asset("clip_timing.json")));
        foreach (var clip in new[] { "talk_a", "joy", "surprise", "affection", "confidence", "disappointment", "idle.gif" })
        {
            using var player = new EmoteClipPlayer(new Image()) { ManualClock = true, Now = () => 0 };
            var decoded = EmoteClipPlayer.Decode(Asset(clip.EndsWith(".gif") ? clip : clip + ".webp"));
            Assert.NotNull(decoded);
            if (clip.EndsWith(".gif")) { player.LoadDecoded(decoded!); player.Tick(decoded!.Durations[0]); Assert.True(player.FrameIndex > 0); player.Tick(decoded.Durations.Sum()); Assert.True(player.IsComplete); continue; }
            Assert.InRange(decoded!.Durations.Length, 80, 100);
            Assert.InRange(decoded.Durations.Sum(), 5900, 6100);
            int firstFrameMs = decoded.Durations[0];
            player.LoadDecoded(decoded);
            player.Tick(firstFrameMs); Assert.True(player.FrameIndex > 0); Assert.False(player.IsComplete);
            player.Tick((int?)timing[clip]!["speakEndMs"] ?? 5600); Assert.InRange(player.FrameIndex, 70, 89);
            player.Tick((int)timing[clip]!["durationMs"]! + 100);
            Assert.True(player.IsComplete); Assert.Equal(player.FrameCount - 1, player.FrameIndex);
        }
    });
    private static Task WithLocalSet(Func<AvatarTubeWindow, string, Task> test) => Run(async () =>
    {
        string root = Path.Combine(TestUserDataProfile.Root, "rain-test"), folder = Path.Combine(root, "resources", "emotes", "set1");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "emotes.json"), "{idleRotation:[{clip:'idle',weight:100},{clip:'alt',weight:0},{clip:'third',weight:-2}],talking:{pool:['talk_a','talk_b']},minClipMs:0,mood:{soft:'alt'},stemPrefix:{greeting:'third'}}");
        foreach (var clip in new[] { "idle", "alt", "third", "talk_a", "talk_b" }) File.WriteAllBytes(Path.Combine(folder, clip + ".gif"), Asset("idle.gif"));
        var settings = new SettingsService(); settings.Current.AvatarGeneration = "v1"; settings.Current.SelectedAvatarSet = 1;
        CoreSettings.ServiceProvider = () => settings;
        CoreMods.ActiveModIdProvider = () => "rain-test";
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "rain-test" }, root, false);
        var tube = new AvatarTubeWindow();
        try { await test(tube, folder); } finally { tube.StopEmoteGeneration(); tube.Close(); }
    });
    [Fact]
    public Task FailedClipFallsBackToIdleWithoutRetryingFailedIdle() => WithLocalSet(async (tube, folder) =>
    {
        await tube.PlayEmote("absent"); Assert.Equal("idle", tube.CurrentEmoteClip);
        File.Delete(Path.Combine(folder, "idle.gif")); await tube.PlayEmote("idle");
        Assert.Equal("idle", tube.CurrentEmoteClip);
    });
    [Fact]
    public Task IdleExcludesCurrentAndFloorsWeights() => WithLocalSet(async (tube, _) =>
    {
        await tube.PlayEmote("idle");
        var picks = Enumerable.Range(0, 100).Select(_ => tube.PickEmoteIdle()).ToArray();
        Assert.DoesNotContain("idle", picks); Assert.Contains("alt", picks); Assert.Contains("third", picks);
    });
    [Fact]
    public Task TextTalkUsesAuthoredJoinsAndOptionalTimingFallback() => WithLocalSet(async (tube, folder) =>
    {
        var events = new System.Collections.Generic.List<(int ms, Action fire)>(); tube.EmoteSchedule = (ms, fire) => events.Add((ms, fire));
        File.WriteAllText(Path.Combine(folder, "clip_timing.json"), "{talk_a:{speakStartMs:120,speakEndMs:2400},talk_b:{speakStartMs:120,speakEndMs:2400}}");
        const string text = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen";
        await tube.PlayEmoteSpeech(text, null, "soft, warm");
        Assert.Equal(4, events.Count); Assert.InRange(events[1].ms - events[0].ms, 2200, 2350);
        events[^1].fire(); Assert.Equal("alt", tube.CurrentEmoteClip);
        File.Delete(Path.Combine(folder, "clip_timing.json")); events.Clear();
        await tube.PlayEmoteSpeech(text, null, null); Assert.True(events.Count >= 2);
        File.WriteAllText(Path.Combine(folder, "clip_timing.json"), "broken"); events.Clear();
        await tube.PlayEmoteSpeech(text, null, null); Assert.True(events.Count >= 2);
        events.Clear(); await tube.PlayEmoteSpeech("mmm, giggles", "greeting_hello.mp3", null);
        Assert.Empty(events); Assert.Equal("third", tube.CurrentEmoteClip);
    });
}
