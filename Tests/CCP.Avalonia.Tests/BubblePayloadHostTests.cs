using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
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
    [InlineData(false), InlineData(true)]
    public void DashboardBoostReachesPlainAndTriggerButNotChaos(bool trigger) => AvaloniaTestDispatcher.Run(() =>
    {
        var s = new AppSettings { BubbleTriggersEnabled = trigger, BubbleTriggerChance = 50, BubbleTriggerVariants = new() { "flash" } };
        AmbientBubble Spawn(int boost) { s.BubbleSpeedBoost = boost; return BubbleOverlay.CreateAmbient(0, new PixelRect(0, 0, 1920, 1080), 1, s, null, true, new Random(1)); }
        var normal = Spawn(0); var fast = Spawn(500);
        Assert.Equal(trigger, fast.AmbientTrigger);
        var y = normal.Y; normal.Step(); var fy = fast.Y; fast.Step();
        Assert.Equal(6 * (y - normal.Y), fy - fast.Y, 6);
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

    [Theory]
    [InlineData(false), InlineData(true)]
    public void BothHeadsRetainThePeakAcrossWeakerShorterPops(bool spiral) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp(); var host = new Window(); var enabled = CoreSettings.Current.SpiralEnabled;
        void Show(int ms, double opacity) { if (spiral) SpiralOverlay.ShowTimed(host, ms, opacity); else PinkFilterOverlay.ShowTimed(host, ms, opacity); }
        try { CoreSettings.Current.SpiralEnabled = false; Show(10000, 0.385); Show(50, 0.2); Assert.Equal(0.385, (spiral ? SpiralOverlay.PayloadHold : PinkFilterOverlay.PayloadHold).Opacity);
            if (spiral) Assert.Equal(0.0385, SpiralOverlay.PaintedOpacity, 6); }
        finally { CoreSettings.Current.SpiralEnabled = enabled; PinkFilterOverlay.CloseAll(); SpiralOverlay.CloseAll(); }
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

    [Fact]
    public void TimedEffectsKeepSettingsAndStopCancelsQueuedWork() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var before = (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled, s.SpiralOpacity);
        try
        {
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
        finally { PinkFilterOverlay.CloseAll(); SpiralOverlay.CloseAll(); BubbleOverlay.Stop(); }
    });
}
