using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubblePayloadHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData(false, "flash"), InlineData(true, "flash"), InlineData(true, "glitch")]
    public void DashboardBoostReachesPlainAndTriggerButNotChaos(bool trigger, string variant) => AvaloniaTestDispatcher.Run(() =>
    {
        var s = new AppSettings { BubbleTriggersEnabled = trigger, BubbleTriggerChance = 50, BubbleTriggerVariants = new() { variant } };
        // CreateAmbient reads more than its Random: the trigger spec's size comes from ChaosBubbleVariants'
        // shared unseeded roll (a draw above ~0.997 unclamps the size factor, ~0.5% of runs), and the
        // trigger cap and Brain Drain pool from the live field and v2 grants. Pin all three so both
        // spawns differ only by the boost.
        var (granted, rng, field) = (PrizeOwnership.IsGranted, ChaosBubbleVariants.Rng, BubbleOverlay.Field.Bubbles.ToArray());
        AmbientBubble normal, fast;
        try
        {
            PrizeOwnership.IsGranted = _ => false;
            BubbleOverlay.Field.Bubbles.Clear();
            AmbientBubble Spawn(int boost)
            {
                s.BubbleSpeedBoost = boost; ChaosBubbleVariants.Rng = new Random(2);
                return BubbleOverlay.CreateAmbient(0, new PixelRect(0, 0, 1920, 1080), 1, s, null, true, new Random(1));
            }
            normal = Spawn(0); fast = Spawn(500);
        }
        finally
        {
            PrizeOwnership.IsGranted = granted; ChaosBubbleVariants.Rng = rng;
            BubbleOverlay.Field.Bubbles.Clear(); BubbleOverlay.Field.Bubbles.AddRange(field);
        }
        Assert.Equal(trigger, fast.AmbientTrigger);
        var y = normal.Y; normal.Step(); var fy = fast.Y; fast.Step();
        // Trigger specs independently roll a size, and larger chaos bubbles move more slowly.
        // Normalize that size factor while still checking the exact sixfold dashboard boost.
        Assert.Equal(6 * (y - normal.Y) / SizeFactor(normal), (fy - fast.Y) / SizeFactor(fast), 6);
        var spec = AmbientBubblePayloads.Build("flash")!;
        var settings = CoreSettings.Current; var saved = settings.BubbleSpeedBoost;
        try
        {
            settings.BubbleSpeedBoost = 0;
            var reference = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1, spec);
            settings.BubbleSpeedBoost = 500;
            var chaos = BubbleOverlay.SpawnChaos(spec, new Random(4));
            chaos.Step(); reference.Step(); Assert.Equal(reference.Y, chaos.Y);
        }
        finally { settings.BubbleSpeedBoost = saved; BubbleOverlay.Stop(); }
    });

    private static double SizeFactor(AmbientBubble b) => b.Spec == null ? 1 : Math.Clamp(1.4 - (b.Size - 150) / 220.0, 0.6, 1.4);

    [Fact]
    public void SpeedBoostComparisonAccountsForDifferentTriggerSizes()
    {
        var normal = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1, new EffectBubbleSpec { SizePx = 130 });
        var fast = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1, new EffectBubbleSpec { SizePx = 156 }, ambientSpeedBoost: 500);
        var y = normal.Y; normal.Step(); var fy = fast.Y; fast.Step();
        Assert.NotEqual(SizeFactor(normal), SizeFactor(fast));
        Assert.Equal(6 * (y - normal.Y) / SizeFactor(normal), (fy - fast.Y) / SizeFactor(fast), 6);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public void BothHeadsRetainThePeakAcrossWeakerShorterPops(bool spiral) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp(); var host = new Window(); var enabled = CoreSettings.Current.SpiralEnabled;
        // A desktop with no compositor (the headless Linux runner) makes the spiral CloseAll, which drops the hold:
        // pin the platform check instead of inheriting whatever an earlier test left in the static.
        var checks = SpiralOverlay.SkipPlatformChecksForTests;
        void Show(int ms, double opacity) { if (spiral) SpiralOverlay.ShowTimed(host, ms, opacity); else PinkFilterOverlay.ShowTimed(host, ms, opacity); }
        try { SpiralOverlay.SkipPlatformChecksForTests = true;
            CoreSettings.Current.SpiralEnabled = false; Show(10000, 0.385); Show(50, 0.2); Assert.Equal(0.385, (spiral ? SpiralOverlay.PayloadHold : PinkFilterOverlay.PayloadHold).Opacity);
            if (spiral) Assert.Equal(0.0385, SpiralOverlay.PaintedOpacity, 6); }
        finally { CoreSettings.Current.SpiralEnabled = enabled; PinkFilterOverlay.CloseAll(); SpiralOverlay.CloseAll(); SpiralOverlay.SkipPlatformChecksForTests = checks; }
    });

    [Fact]
    public void TriggerSpawnCapAndAmbientPopStaySeparateFromChaos() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var previousFreeze = CoreSubliminal.BambiFreezeProvider;
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100,
            BubbleTriggerVariants = new() { "flash" } };
        try
        {
            BubbleOverlay.Field.Bubbles.Clear();
            var b = BubbleOverlay.CreateAmbient(0, new PixelRect(0, 0, 1280, 720), 1, s, null, false, new Random(1));
            Assert.True(b.AmbientTrigger); Assert.False(b.Clickable);
            BubbleOverlay.Field.Bubbles.Add(b);
            Assert.Equal(0, BubbleOverlay.ChaosCount);
            for (var i = 0; i < 3; i++) BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { Spec = b.Spec });
            Assert.False(BubbleOverlay.CreateAmbient(0, new PixelRect(0, 0, 1280, 720), 1, s, null, true).AmbientTrigger);
            var running = typeof(BubbleOverlay).GetField("_running", BindingFlags.Static | BindingFlags.NonPublic)!;
            running.SetValue(null, true);
            var calls = 0;
            CoreSubliminal.BambiFreezeProvider = () => calls++;
            b.Spec = new EffectBubbleSpec { Payload = new BambiFreezePayload() };
            BubbleOverlay.Pop(b); BubbleOverlay.Pop(b); Assert.Equal(1, calls);
        }
        finally { CoreSubliminal.BambiFreezeProvider = previousFreeze; BubbleOverlay.Stop(); }
    });

    private sealed class FeedHost : IMandatoryVideoHost
    {
        public int FloatingLimit => 1; public int FloatingUp => 0;
        public bool FeedOwnsTheScreen => true;
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }

    // Restack b174 (#3942): WPF VideoService.cs:2311 plays a bubble-earned video over a visible feed.
    [Fact]
    public void PoppedVideoBubblePlaysOverAVisibleFeed() => AvaloniaTestDispatcher.Run(() =>
    {
        var previous = CoreEngine.Video;
        var video = new MandatoryVideoScheduler(new FeedHost(), TimeProvider.System, () => new[] { "/v/popped.mp4" });
        CoreEngine.Video = video;
        try
        {
            BubblePayloadHost.Fire(new VideoPayload());
            Assert.True(video.IsPlaying); // pre-roll started, not held behind the feed
            video.ForceCleanup();
            Assert.True(video.Trigger(silentIfEmpty: true)); // a non-earned request is held
            Assert.False(video.IsPlaying);
        }
        finally { video.Stop(); CoreEngine.Video = previous; }
    });

    [Fact]
    public void TimedEffectsKeepSettingsAndStopCancelsQueuedWork() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var before = (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled, s.SpiralOpacity);
        var checks = SpiralOverlay.SkipPlatformChecksForTests;   // see BothHeadsRetainThePeakAcrossWeakerShorterPops
        try
        {
            SpiralOverlay.SkipPlatformChecksForTests = true;
            var host = new Window();
            PinkFilterOverlay.ShowTimed(host, 10000, 0.5);
            SpiralOverlay.ShowTimed(host, 10000, 0.5);
            Assert.True(PinkFilterOverlay.HasTimedPayload); Assert.True(SpiralOverlay.HasTimedPayload);
            Assert.Equal(before, (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled, s.SpiralOpacity));
            Action? queued = null; var fired = false;
            BubbleOverlay.Payloads.Run(new VideoPayload(), () => true, () => false, () => false,
                a => queued = a, _ => throw new Exception(), _ => fired = true);
            PanicSurfaces.All.Single(x => x.Id == "bubble-host").Stop(null);
            queued!(); Assert.False(fired);
            Assert.False(PinkFilterOverlay.HasTimedPayload); Assert.False(SpiralOverlay.HasTimedPayload);
        }
        finally { PinkFilterOverlay.CloseAll(); SpiralOverlay.CloseAll(); BubbleOverlay.Stop(); SpiralOverlay.SkipPlatformChecksForTests = checks; }
    });
}
