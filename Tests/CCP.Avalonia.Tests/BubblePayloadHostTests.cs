using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubblePayloadHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void RedPressIsCaughtAndReleasePopsOnlyOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var field = BubbleOverlay.Field;
        var clock = field.Clock; var callback = field.NatashaPopped;
        var now = new DateTime(2026, 1, 1); var pops = 0;
        var b = new AmbientBubble { Screen = 0, X = 400, Y = 300, Size = 200, Natasha = true, Clickable = true };
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1280, 720), 1) { Width = 1280, Height = 720 };
        try
        {
            field.Bubbles.Clear(); field.Bubbles.Add(b); field.Clock = () => now;
            field.NatashaPopped = bubble => { pops++; bubble.Popping = true; };
            w.Show();
            w.MouseDown(new Point(500, 400), MouseButton.Left);
            Assert.True(b.Resisting); Assert.False(b.Popping);
            now = now.AddMilliseconds(300);
            w.MouseUp(new Point(500, 400), MouseButton.Left);
            field.Step(); field.Step();
            Assert.Equal(1, pops);
            Assert.True(b.Popping);
        }
        finally
        {
            w.Close(); field.Bubbles.Clear(); field.Clock = clock;
            field.NatashaPopped = callback; field.Held = false;
        }
    });

    [Fact]
    public void TimedTintDoesNotChangeSettingsAndPanicDropsItsTimer() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = global::ConditioningControlPanel.CoreSettings.Current;
        var enabled = s.PinkFilterEnabled; var opacity = s.PinkFilterOpacity;
        var host = new Window();
        PinkFilterOverlay.ShowTimed(host, 10000, 0.5);
        Assert.True(PinkFilterOverlay.HasTimedPayload);
        Assert.Equal(enabled, s.PinkFilterEnabled);
        Assert.Equal(opacity, s.PinkFilterOpacity);
        PinkFilterOverlay.CloseAll();
        Assert.False(PinkFilterOverlay.HasTimedPayload);
    });

    [Fact]
    public void PanicCancelsQueuedPayloadAndClearsSharedHost() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        PinkFilterOverlay.ShowTimed(new Window(), 10000, 0.5);
        Action? queued = null; var fired = false;
        BubbleOverlay.Payloads.Run(new VideoPayload(), () => true, () => false, () => false,
            a => queued = a, _ => throw new Exception(), _ => fired = true);
        BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { Natasha = true });
        PanicSurfaces.All.Single(s => s.Id == "bubble-host").Stop(null);
        queued!();
        Assert.False(fired);
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        Assert.False(PinkFilterOverlay.HasTimedPayload);   // panic ends the timed tint (WPF OverlayService.Stop)
    });
}
