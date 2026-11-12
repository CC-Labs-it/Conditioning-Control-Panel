using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NatashaBubbleTests
{
    private sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => Math.Min(value, maxValue - 1);
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task TriggerPopsRunTheirOverlays() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var (oldHost, oldRun, oldSchedule, oldFirer) = (BubbleOverlay.EffectHost, BubbleOverlay.RunOverlay, TimedOverlayHold.Schedule, EffectPayload.Firer);
        var (live, settings) = (CoreSettings.Current, new ConditioningControlPanel.Models.AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100 });
        var (oldDay, oldPaid, runs, timers, fired) = (live.AmbientBubbleXpDayKey, live.AmbientBubbleXpPaidToday, new List<string>(), new List<Action>(), 0);
        try
        {
            BubbleOverlay.EffectHost = () => new Border(); TimedOverlayHold.Schedule = (a, _) => timers.Add(a); EffectPayload.Firer = _ => fired++;
            BubbleOverlay.RunOverlay = (kind, _, ms, opacity) => runs.Add($"{kind}:{ms}:{opacity:0.00}");
            foreach (var id in new[] { "pink", "spiral", "glitch", BrainDrainBubble.VariantId })
            {
                settings.BubbleTriggerVariants = new List<string> { id };
                var spec = BubbleOverlay.RollAmbientTrigger(settings, new FixedRandom(0), true, 0)!;
                spec.Payload.Strength = 50;   // the size roll is random; WPF EffectPayloadFiring.cs:54,63 scale from it
                BubbleOverlay.PopByPlayer(new AmbientBubble { Clickable = true, IsAmbientTrigger = true, Spec = spec });
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(new[] { "pink_filter:7500:0.00", "spiral:7500:0.00", "braindrain:3600:0.30" }, runs);
            Assert.Equal((0.475, 0.475, 0), (PinkFilterOverlay.Timed.Opacity!.Value, SpiralOverlay.Timed.Opacity!.Value, fired));   // the drain pays XP only
            foreach (var release in timers) release();
            Assert.Equal((null, null, 5), (PinkFilterOverlay.Timed.Opacity, SpiralOverlay.Timed.Opacity, runs.Count));
        }
        finally
        {
            (BubbleOverlay.EffectHost, BubbleOverlay.RunOverlay, TimedOverlayHold.Schedule, EffectPayload.Firer) = (oldHost, oldRun, oldSchedule, oldFirer);
            (live.AmbientBubbleXpDayKey, live.AmbientBubbleXpPaidToday) = (oldDay, oldPaid);
            PinkFilterOverlay.Timed.Clear(); SpiralOverlay.Timed.Clear(); BubbleOverlay.Stop();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void TriggerPoolAndNatashaWeightFollowSpawnSettings()
    {
        var s = new ConditioningControlPanel.Models.AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 50, BubbleTriggerVariants = new List<string> { "flash" }, ChasterNatashaTriggerWeight = 1 };
        Assert.Null(BubbleOverlay.RollAmbientTrigger(s, new FixedRandom(99), false, 0));
        Assert.Null(BubbleOverlay.RollAmbientTrigger(s, new FixedRandom(0), false, 4));
        var spec = Assert.IsType<EffectBubbleSpec>(BubbleOverlay.RollAmbientTrigger(s, new FixedRandom(0), false, 0));
        Assert.True(spec.Payload.Ambient); Assert.False(spec.IsLive);
        var bubble = new AmbientBubble { Spec = spec, IsAmbientTrigger = true };
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), true); Assert.False(bubble.IsNatasha);
        s.ChasterNatashaTriggerWeight = 10;
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), true); Assert.True(bubble.IsNatasha);
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), false); Assert.False(bubble.IsNatasha);
        bubble.IsAmbientTrigger = false;
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), true); Assert.False(bubble.IsNatasha);
    }

    [Theory]
    [InlineData("RainDown")]
    [InlineData("RoamBounce")]
    public void TriggerUsesSelectedChaosMotion(string motion)
    {
        var settings = new ConditioningControlPanel.Models.AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100, BubbleTriggerVariants = new List<string> { "flash" }, ChaosMotionMode = motion };
        var spec = Assert.IsType<EffectBubbleSpec>(BubbleOverlay.RollAmbientTrigger(settings, new FixedRandom(0), false, 0));
        Assert.Equal(Enum.Parse<ChaosMotion>(motion), spec.Motion);
        var bubble = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1, spec);
        bubble.IsAmbientTrigger = true; var initialY = bubble.Y; bubble.Step(false);
        Assert.True(motion == "RainDown" ? bubble.Y > initialY : bubble.Y != initialY);
    }

    [Fact]
    public Task StopInvalidatesQueuedTriggerPop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var (oldFirer, fired) = (EffectPayload.Firer, 0);
        try
        {
            EffectPayload.Firer = _ => fired++;
            BubbleOverlay.PopByPlayer(new AmbientBubble { Clickable = true, IsAmbientTrigger = true, Spec = new EffectBubbleSpec { Payload = new GifCascadePayload() } });
            BubbleOverlay.Stop(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, fired);
        }
        finally { EffectPayload.Firer = oldFirer; BubbleOverlay.Stop(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RedClickFiresPayloadAndBooksWhileHoldCreditsWithoutPop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var (field, settings, oldBook, oldFirer) = (BubbleOverlay.Field, CoreSettings.Current, BubbleOverlay.BookNatasha, EffectPayload.Firer);
        var (oldClock, oldDay, oldPaid) = (field.Clock, settings.AmbientBubbleXpDayKey, settings.AmbientBubbleXpPaidToday);
        var (now, booked, fired) = (DateTime.UtcNow, new List<string>(), 0);
        try
        {
            field.Bubbles.Clear(); field.Clock = () => now;
            BubbleOverlay.BookNatasha = (row, _) => booked.Add(row); EffectPayload.Firer = _ => fired++;
            var b = new AmbientBubble { X = 0, Y = 0, Size = 100, Clickable = true, IsNatasha = true, IsAmbientTrigger = true, Spec = new EffectBubbleSpec { Payload = new GifCascadePayload() } };
            field.Bubbles.Add(b); BubbleOverlay.Press(b, 50, 50); BubbleOverlay.PointerHold(false, 50, 50);
            BubbleOverlay.TickNatashaHold(); Dispatcher.UIThread.RunJobs();
            Assert.True(b.Popping); Assert.Equal(new[] { NatashasFavourite.EventId }, booked); Assert.Equal(1, fired);
            BubbleOverlay.PopByPlayer(b); Assert.Single(booked);
            b = new AmbientBubble { X = 0, Y = 0, Size = 100, Clickable = true, IsNatasha = true, IsAmbientTrigger = true, Spec = new EffectBubbleSpec { Payload = new GifCascadePayload() } };
            field.Bubbles.Add(b); BubbleOverlay.Press(b, 50, 50);
            BubbleOverlay.PopByPlayer(b); // gaze cannot interrupt a hold
            Assert.False(b.Popping);
            now = now.AddMilliseconds(NatashasFavourite.HoldMs);
            BubbleOverlay.TickNatashaHold(); Dispatcher.UIThread.RunJobs();
            Assert.True(b.Deflating); Assert.Equal(new[] { NatashasFavourite.EventId, NatashasFavourite.HeldEventId }, booked); Assert.Equal(1, fired);
            b = new AmbientBubble { X = 0, Y = 0, Size = 100, Clickable = true, IsNatasha = true };
            field.Bubbles.Add(b); BubbleOverlay.Press(b, 50, 50); BubbleOverlay.PointerHold(true, 200, 200); BubbleOverlay.TickNatashaHold();
            Assert.False(b.Popping); Assert.Equal(2, booked.Count);
            BubbleOverlay.Pop(b); // programmatic pops never book
            Assert.Equal(2, booked.Count);
        }
        finally
        {
            field.Bubbles.Clear(); field.Held = false; BubbleOverlay.TickNatashaHold(); field.Clock = oldClock;
            BubbleOverlay.BookNatasha = oldBook; EffectPayload.Firer = oldFirer;
            settings.AmbientBubbleXpDayKey = oldDay; settings.AmbientBubbleXpPaidToday = oldPaid;
        }
        return Task.CompletedTask;
    });
}
