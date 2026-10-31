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
        var tube = new AvatarTubeWindow(null);
        try
        {
            var sets = tube.PickableAvatarSets();
            Assert.Equal(new[] { 1 }, sets);
            tube.UpdateGenerationPill();
            Assert.True(tube.FindControl<Border>("BtnAvatarGeneration")!.IsHitTestVisible);
            tube.FlipEmoteGeneration();
            Assert.Equal("v2", service.Current.AvatarGeneration);
            Assert.Equal("rain_emotes", tube.CurrentEmoteFolder);
            Assert.Equal(240, tube.FindControl<Border>("AvatarBorder")!.Margin.Bottom);
            Assert.Equal(0.84, Assert.IsType<global::Avalonia.Media.ScaleTransform>(tube.FindControl<Image>("ImgAvatarAnimated")!.RenderTransform).ScaleX);
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
            player.Stop(); Assert.False(player.IsReady); Assert.Equal(0, player.FrameCount);
        }
    });
    private static Task WithLocalSet(Func<AvatarTubeWindow, string, Task> test) => Run(async () =>
    {
        string root = Path.Combine(TestUserDataProfile.Root, "rain-test"), folder = Path.Combine(root, "resources", "emotes", "set1");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "emotes.json"), "{idleRotation:[{clip:'idle',weight:100},{clip:'alt',weight:0},{clip:'third',weight:-2}],talking:{pool:['talk_a','talk_b']},minClipMs:0,layout:{scale:0.8,offsetX:11,offsetY:13,detachedX:17,detachedY:19},clipScale:{idle:0.5},mood:{soft:'alt'},stemPrefix:{greeting:'third'}}");
        foreach (var clip in new[] { "idle", "alt", "third", "talk_a", "talk_b" }) File.WriteAllBytes(Path.Combine(folder, clip + ".gif"), Asset("idle.gif"));
        var settings = new SettingsService(); settings.Current.AvatarGeneration = "v1"; settings.Current.SelectedAvatarSet = 1;
        settings.Current.TubeLayoutOverridesByMod = new() { ["rain-test"] = new ModTubeLayout { AvatarScale = 1.5, AvatarOffsetX = 20, AvatarOffsetY = 30, AvatarDetachedOffsetX = 40, AvatarDetachedOffsetY = 50 } };
        CoreSettings.ServiceProvider = () => settings;
        CoreMods.ActiveModIdProvider = () => "rain-test";
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "rain-test" }, root, false);
        var tube = new AvatarTubeWindow(null);
        try { await test(tube, folder); } finally { tube.StopEmoteGeneration(); tube.Close(); }
    });
    [Fact]
    public Task FailedClipFallsBackToIdleWithoutRetryingFailedIdle() => WithLocalSet(async (tube, folder) =>
    {
        await tube.PlayEmote("absent"); Assert.Equal("idle", tube.CurrentEmoteClip);
        using (File.Open(Path.Combine(folder, "idle.gif"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        File.Delete(Path.Combine(folder, "idle.gif")); await tube.PlayEmote("idle");
        Assert.Equal("idle", tube.CurrentEmoteClip);
    });
    [Fact]
    public Task IdleExcludesCurrentAndFloorsWeights() => WithLocalSet(async (tube, _) =>
    {
        await tube.PlayEmote("idle");
        Assert.False(tube.FindControl<Image>("ImgAvatar")!.IsVisible);
        Assert.True(new[] { "ImgAvatarAnimated", "ImgAvatarAnimatedB" }.Any(n => tube.FindControl<Image>(n) is { IsVisible: true, Source: not null }));
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
        await tube.PlayEmoteSpeech(text, null, null);
        tube.StopEmoteGeneration(); foreach (var e in events) e.fire();
        Assert.Null(tube.CurrentEmoteClip); Assert.Null(tube.CurrentEmoteFolder);
    });
    [Fact]
    public Task EmotesComposeWithModLayoutAndSurviveRefresh() => WithLocalSet(async (tube, _) =>
    {
        await tube.PlayEmote("idle"); tube.Attach();
        var border = tube.FindControl<Border>("AvatarBorder")!;
        Assert.Equal(new Thickness(5, 100, 95, 227), border.Margin);
        tube.Detach(); tube.RefreshTubeLayout();
        Assert.Equal(new Thickness(5, 100, 379, 259), border.Margin);
        var image = new[] { "ImgAvatarAnimated", "ImgAvatarAnimatedB" }.Select(n => tube.FindControl<Image>(n)!).First(i => i.Opacity == 1);
        Assert.Equal(0.6, Assert.IsType<global::Avalonia.Media.ScaleTransform>(image.RenderTransform).ScaleX, 6);
    });
    [Fact]
    public Task ImmediateAndQueuedPresetsReplaceTalkAfterSuppression() => WithLocalSet(async (tube, _) =>
    {
        var events = new System.Collections.Generic.List<Action>(); tube.EmoteSchedule = (_, fire) => events.Add(fire);
        tube.Giggle("one two three four five six seven eight nine ten");
        Assert.NotEmpty(events); var stale = events.ToArray(); events.Clear();
        tube.Giggle("mmm"); Assert.Equal(1, tube.QueuedSpeechCount); Assert.Empty(events);
        Action? gap = null; tube.SpeechDelayScheduler = (_, fire) => { gap = fire; return () => { }; };
        tube.EndSpeechBubble(); Assert.NotNull(gap); gap!();
        Assert.Empty(events); var reaction = tube.CurrentEmoteClip; Assert.Contains(reaction, new[] { "alt", "third" });
        foreach (var fire in stale) fire(); Assert.Equal(reaction, tube.CurrentEmoteClip);
        Assert.Equal("mmm", tube.FindControl<TextBlock>("TxtSpeech")!.Text); await Task.CompletedTask;
    });
}
