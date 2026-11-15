using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleAvatarEggTests
{
    private sealed class Hit : Random { public override int Next(int maxValue) => 0; }
    private static AmbientBubble NewBubble(string id = "flash") => new()
    {
        AmbientTrigger = true, Spec = new EffectBubbleSpec { VariantId = id, TreatLifeMs = 7000, Payload = new SubliminalPayload { Ambient = true } }
    };

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void SettingsChaosAndVideoGateTheScan(bool enabled, bool triggers, bool chaos, bool video)
    {
        Assert.False(BubbleAvatarEgg.Gates(enabled, triggers, chaos, video));
        Assert.True(BubbleAvatarEgg.Gates(true, true, false, false));
    }

    [Fact]
    public async Task HitGlidesSpeaksPopsOnceAndReturnsWithCooldown()
    {
        var b = NewBubble(); var bubbles = new List<AmbientBubble> { b };
        var now = DateTime.UtcNow.AddSeconds(5); var calls = new List<string>();
        var avatar = new BubbleAvatarEgg.Avatar((bubble, ct) => { Assert.True(bubble.ClaimedByAvatar); calls.Add("glide"); return Task.CompletedTask; },
            () => { calls.Add("home"); return Task.CompletedTask; }, (text, audio) => { calls.Add(text); Assert.Equal("clip", audio); });
        var egg = new BubbleAvatarEgg(() => true, () => avatar, bubbles, new Hit(), () => now,
            id => { calls.Add(id); return id == "egg_avatar_pop_generic" ? ("generic line", "clip") : null; },
            path => Task.FromResult<double?>(2), (bubble, volume) => { calls.Add("pop"); Assert.Equal(1.5f, volume); Assert.False(bubble.ClaimedByAvatar); bubble.Popping = true; });
        egg.Delay = (ms, ct) => { calls.Add($"delay:{ms}"); return Task.CompletedTask; };
        egg.Scan(); await egg.Running!;
        Assert.Equal(new[] { "glide", "egg_avatar_pop_flash", "egg_avatar_pop_generic", "generic line", "delay:2900", "pop", "delay:2500", "home" }, calls);
        Assert.False(b.ClaimedByAvatar);
        var next = NewBubble(); bubbles.Add(next); now = now.AddSeconds(59); egg.Scan();
        Assert.False(next.ClaimedByAvatar); Assert.Equal(8, calls.Count);
        now = now.AddSeconds(2); egg.Scan(); await egg.Running!;
        Assert.True(next.Popping);
    }

    [Fact]
    public async Task UnavailableAvatarAndCancellationReleaseTheClaimAndReturnHome()
    {
        var b = NewBubble(); var bubbles = new List<AmbientBubble> { b }; var now = DateTime.UtcNow.AddSeconds(5);
        bool available = false; int home = 0, pops = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var avatar = new BubbleAvatarEgg.Avatar(async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); },
            () => { home++; return Task.CompletedTask; }, (_, _) => throw new Exception("unexpected speech"));
        var egg = new BubbleAvatarEgg(() => true, () => available ? avatar : null, bubbles, new Hit(), () => now,
            _ => null, _ => Task.FromResult<double?>(null), (_, _) => pops++);
        egg.Scan(); Assert.False(b.ClaimedByAvatar); Assert.Null(egg.Running);
        available = true; egg.Scan(); await started.Task; Assert.True(b.ClaimedByAvatar);
        egg.Cancel(); Assert.False(b.ClaimedByAvatar); await egg.Running!;
        Assert.Equal(1, home); Assert.Equal(0, pops);
        var next = NewBubble(); bubbles.Add(next); egg.Scan(); Assert.False(next.ClaimedByAvatar);
    }

    [Fact]
    public async Task UnknownLineUsesInlineFallbackAndReadingTime()
    {
        var b = NewBubble("unknown"); var now = DateTime.UtcNow.AddSeconds(5);
        string? spoken = null; var delays = new List<int>();
        var avatar = new BubbleAvatarEgg.Avatar((_, _) => Task.CompletedTask, () => Task.CompletedTask,
            (text, audio) => { spoken = text; Assert.Null(audio); });
        var egg = new BubbleAvatarEgg(() => true, () => avatar, new List<AmbientBubble> { b }, new Hit(), () => now,
            _ => null, _ => throw new Exception(), (bubble, _) => bubble.Popping = true);
        egg.Delay = (ms, ct) => { delays.Add(ms); return Task.CompletedTask; };
        egg.Scan(); await egg.Running!;
        Assert.Equal("Ooh, let me pop this one for you~", spoken);
        Assert.Equal(Math.Clamp(spoken!.Length * 55 + 600, 1500, 4500), delays[0]);
        Assert.Equal(2500, delays[1]); Assert.True(b.Popping);
    }
    [Fact]
    public async Task FrameStartsEggAndStopCancelsTheClaim() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var host = new Window(); var b = NewBubble(); int home = 0;
        var oldChecks = BubbleOverlay.SkipPlatformChecksForTests;
        var avatar = new BubbleAvatarEgg.Avatar(async (_, ct) => await Task.Delay(Timeout.Infinite, ct),
            () => { home++; return Task.CompletedTask; }, (_, _) => { });
        var egg = new BubbleAvatarEgg(() => true, () => avatar, BubbleOverlay.Field.Bubbles, new Hit(),
            () => DateTime.UtcNow.AddSeconds(5), _ => null, _ => Task.FromResult<double?>(null), (_, _) => { });
        try
        {
            BubbleOverlay.SkipPlatformChecksForTests = true;
            BubbleOverlay.EggOverride = egg;
            host.Show(); BubbleOverlay.Start(host);
            BubbleOverlay.Field.Bubbles.Clear(); BubbleOverlay.Field.Bubbles.Add(b);
            BubbleOverlay.OnFrame(TimeSpan.FromSeconds(100));
            Assert.True(b.ClaimedByAvatar);
            BubbleOverlay.Stop();
            Assert.False(b.ClaimedByAvatar);
            await egg.Running!;
            Assert.Equal(1, home);
        }
        finally
        {
            BubbleOverlay.Stop(); BubbleOverlay.EggOverride = null;
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks; host.Close();
        }
    });
    [Fact]
    public async Task FramePopRunsTheAmbientPayloadAndRewardOnce() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var host = new Window(); var b = NewBubble(); int delivered = 0, popped = 0;
        var soundPath = System.IO.Path.GetTempFileName(); var volumes = new List<float>();
        var oldAudio = CoreAudio.PlayOneShotProvider; var oldOverride = CoreModArt.AudioOverridePathProvider;
        var oldMaster = CoreSettings.Current.MasterVolume; var oldBubbleVolume = CoreSettings.Current.BubblesVolume;
        var oldChecks = BubbleOverlay.SkipPlatformChecksForTests;
        var oldPool = CoreSettings.Current.SubliminalPool; var oldShow = CoreSubliminal.ShowProvider;
        var oldDay = CoreSettings.Current.AmbientBubbleXpDayKey; var oldPaid = CoreSettings.Current.AmbientBubbleXpPaidToday;
        var avatar = new BubbleAvatarEgg.Avatar((_, _) => Task.CompletedTask, () => Task.CompletedTask, (_, _) => { });
        var egg = new BubbleAvatarEgg(() => true, () => avatar, BubbleOverlay.Field.Bubbles, new Hit(),
            () => DateTime.UtcNow.AddSeconds(5), _ => null, _ => Task.FromResult<double?>(null), BubbleOverlay.Pop);
        egg.Delay = (_, _) => Task.CompletedTask;
        void Popped() => popped++;
        try
        {
            AmbientBubbleXp.Take(CoreSettings.Current, AmbientBubbleXp.DailyXpCap);
            CoreSettings.Current.MasterVolume = 40; CoreSettings.Current.BubblesVolume = 50;
            CoreModArt.AudioOverridePathProvider = _ => soundPath;
            CoreAudio.PlayOneShotProvider = (_, volume, _, _, _) => volumes.Add(volume);
            CoreSettings.Current.SubliminalPool = new Dictionary<string, bool> { ["test"] = true };
            CoreSubliminal.ShowProvider = _ => delivered++;
            BubbleOverlay.Popped += Popped;
            BubbleOverlay.SkipPlatformChecksForTests = true; BubbleOverlay.EggOverride = egg;
            host.Show(); BubbleOverlay.Start(host);
            BubbleOverlay.Field.Bubbles.Clear(); BubbleOverlay.Field.Bubbles.Add(b);
            BubbleOverlay.OnFrame(TimeSpan.FromSeconds(100)); await egg.Running!;
            BubbleOverlay.Pop(b);
            Assert.True(b.Popping); Assert.Equal(1, popped); Assert.Equal(1, delivered);
            Assert.Single(volumes); Assert.Equal((float)Math.Pow(.4f * .5f, 1.5) * 1.5f, volumes[0], 5);
        }
        finally
        {
            BubbleOverlay.Stop(); BubbleOverlay.EggOverride = null; BubbleOverlay.Popped -= Popped;
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks;
            CoreSettings.Current.SubliminalPool = oldPool; CoreSubliminal.ShowProvider = oldShow;
            CoreSettings.Current.MasterVolume = oldMaster; CoreSettings.Current.BubblesVolume = oldBubbleVolume;
            CoreAudio.PlayOneShotProvider = oldAudio; CoreModArt.AudioOverridePathProvider = oldOverride;
            System.IO.File.Delete(soundPath);
            CoreSettings.Current.AmbientBubbleXpDayKey = oldDay; CoreSettings.Current.AmbientBubbleXpPaidToday = oldPaid; host.Close();
        }
    });
    [Fact]
    public async Task TubeRejectsUnsupportedMovementAndRestoresItsOwnHome() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.AvatarEnabled = true; CoreSettings.Current.AvatarTubeDetached = true;
        var tube = new AvatarTubeWindow(null) { PossessionMoveSupportedOverride = false };
        try
        {
            tube.Show(); tube.Position = new PixelPoint(200, 250);
            Assert.False(tube.CanPerformBubbleEgg);
            tube.PossessionMoveSupportedOverride = true;
            Assert.True(tube.CanPerformBubbleEgg);
            var b = NewBubble(); b.X = 500; b.Y = 300; b.Size = 100;
            using var cancel = new CancellationTokenSource(70);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tube.GlideToBubbleAsync(b, cancel.Token));
            Assert.False(tube.CanPerformPossessionMove);
            await tube.ReturnFromBubbleAsync();
            Assert.Equal(new PixelPoint(200, 250), tube.Position);
            Assert.True(tube.CanPerformPossessionMove);
        }
        finally { tube.Close(); CoreSettings.ServiceProvider = previous; }
    });
}
