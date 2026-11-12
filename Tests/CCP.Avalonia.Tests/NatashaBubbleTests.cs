using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
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

    [Fact]
    public void TriggerPoolAndNatashaWeightFollowSpawnSettings()
    {
        var s = new ConditioningControlPanel.Models.AppSettings
        {
            BubbleTriggersEnabled = true, BubbleTriggerChance = 50,
            BubbleTriggerVariants = new List<string> { "flash" }, ChasterNatashaTriggerWeight = 1,
        };
        Assert.Null(BubbleOverlay.RollAmbientTrigger(s, new FixedRandom(99), false, 0));
        Assert.Null(BubbleOverlay.RollAmbientTrigger(s, new FixedRandom(0), false, 4));
        var spec = Assert.IsType<EffectBubbleSpec>(BubbleOverlay.RollAmbientTrigger(s, new FixedRandom(0), false, 0));
        Assert.True(spec.Payload.Ambient);
        Assert.False(spec.IsLive);
        var bubble = new AmbientBubble { Spec = spec, IsAmbientTrigger = true };
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), true);
        Assert.False(bubble.IsNatasha);
        s.ChasterNatashaTriggerWeight = 10;
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), true);
        Assert.True(bubble.IsNatasha);
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), false);
        Assert.False(bubble.IsNatasha);
        bubble.IsAmbientTrigger = false;
        BubbleOverlay.MarkNatasha(bubble, s, new FixedRandom(5), true);
        Assert.False(bubble.IsNatasha);
    }

    [Fact]
    public Task StopInvalidatesQueuedTriggerPop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldFirer = EffectPayload.Firer;
        var fired = 0;
        try
        {
            EffectPayload.Firer = _ => fired++;
            var b = new AmbientBubble { Clickable = true, IsAmbientTrigger = true, Spec = new EffectBubbleSpec { Payload = new GifCascadePayload() } };
            BubbleOverlay.PopByPlayer(b);
            BubbleOverlay.Stop();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, fired);
        }
        finally { EffectPayload.Firer = oldFirer; BubbleOverlay.Stop(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RedClickFiresPayloadAndBooksWhileHoldCreditsWithoutPop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var field = BubbleOverlay.Field;
        var oldClock = field.Clock;
        var oldBook = BubbleOverlay.BookNatasha;
        var oldFirer = EffectPayload.Firer;
        var settings = CoreSettings.Current;
        var oldDay = settings.AmbientBubbleXpDayKey;
        var oldPaid = settings.AmbientBubbleXpPaidToday;
        var now = DateTime.UtcNow;
        var booked = new List<string>();
        var fired = 0;
        try
        {
            field.Bubbles.Clear(); field.Clock = () => now;
            BubbleOverlay.BookNatasha = (row, _) => booked.Add(row);
            EffectPayload.Firer = _ => fired++;
            var b = new AmbientBubble { X = 0, Y = 0, Size = 100, Clickable = true, IsNatasha = true, IsAmbientTrigger = true, Spec = new EffectBubbleSpec { Payload = new GifCascadePayload() } };
            field.Bubbles.Add(b);
            BubbleOverlay.Press(b, 50, 50);
            BubbleOverlay.PointerHold(false, 50, 50);
            BubbleOverlay.TickNatashaHold();
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.Popping);
            Assert.Equal(new[] { NatashasFavourite.EventId }, booked);
            Assert.Equal(1, fired);
            BubbleOverlay.PopByPlayer(b);
            Assert.Single(booked);
            b = new AmbientBubble { X = 0, Y = 0, Size = 100, Clickable = true, IsNatasha = true, IsAmbientTrigger = true, Spec = new EffectBubbleSpec { Payload = new GifCascadePayload() } };
            field.Bubbles.Add(b);
            BubbleOverlay.Press(b, 50, 50);
            BubbleOverlay.PopByPlayer(b); // gaze cannot interrupt a hold
            Assert.False(b.Popping);
            now = now.AddMilliseconds(NatashasFavourite.HoldMs);
            BubbleOverlay.TickNatashaHold();
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.Deflating);
            Assert.Equal(new[] { NatashasFavourite.EventId, NatashasFavourite.HeldEventId }, booked);
            Assert.Equal(1, fired);
            b = new AmbientBubble { X = 0, Y = 0, Size = 100, Clickable = true, IsNatasha = true };
            field.Bubbles.Add(b);
            BubbleOverlay.Press(b, 50, 50);
            BubbleOverlay.PointerHold(true, 200, 200);
            BubbleOverlay.TickNatashaHold();
            Assert.False(b.Popping);
            Assert.Equal(2, booked.Count);
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
