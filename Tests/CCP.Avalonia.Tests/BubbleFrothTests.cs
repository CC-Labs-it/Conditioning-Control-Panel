using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform.Super;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleFrothTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void Cursor_push_obeys_setting(int push, bool moves) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEffect = BubbleOverlay.FrothEffectOn;
        var oldChecks = BubbleOverlay.SkipPlatformChecksForTests;
        var oldPointer = BubbleOverlay.FrothPointer;
        var settings = CoreSettings.Current;
        int oldPush = settings.FrothCursorPush;
        var host = new Window();
        try
        {
            BubbleOverlay.SkipPlatformChecksForTests = true;
            BubbleOverlay.FrothEffectOn = _ => true;
            settings.FrothCursorPush = push;
            host.Show(); BubbleOverlay.Start(host);
            BubbleOverlay.Field.Bubbles.Clear();
            var b = new AmbientBubble { X = 300, Y = 300, Size = 100 };
            BubbleOverlay.Field.Bubbles.Add(b);
            var c = BubbleOverlay.CenterPx(b);
            var scale = BubbleOverlay.ScaleFor(b);
            BubbleOverlay.FrothPointer = () => new PixelPoint((int)(c.X - 75 * scale), (int)c.Y);
            BubbleOverlay.StepFrothMotion(.1);
            Assert.Equal(moves, b.X > 300);
        }
        finally
        {
            BubbleOverlay.Stop(); host.Close(); settings.FrothCursorPush = oldPush;
            BubbleOverlay.FrothPointer = oldPointer; BubbleOverlay.FrothEffectOn = oldEffect;
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks;
        }
    });

    [Fact]
    public void Live_pool_pulls_and_shrinks_a_bubble() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEffect = BubbleOverlay.FrothEffectOn;
        var oldChecks = BubbleOverlay.SkipPlatformChecksForTests;
        var oldPointer = BubbleOverlay.FrothPointer;
        var stage = SuperStageHost.Stage;
        bool oldPoolOn = stage.PoolOn, oldDrag = stage.DragsBubbles;
        var host = new Window();
        try
        {
            BubbleOverlay.SkipPlatformChecksForTests = true; BubbleOverlay.FrothEffectOn = _ => true;
            BubbleOverlay.FrothPointer = () => null;
            host.Show(); BubbleOverlay.Start(host);
            BubbleOverlay.Field.Bubbles.Clear();
            var b = new AmbientBubble { X = 300, Y = 300, Size = 100 };
            BubbleOverlay.Field.Bubbles.Add(b);
            var c = BubbleOverlay.CenterPx(b); var scale = BubbleOverlay.ScaleFor(b);
            stage.Pool.Reset(); stage.PoolOn = true; stage.DragsBubbles = true;
            stage.Pool.Step(5, c.X + 50 * scale, c.Y, 0, scale, 1000 * scale);
            BubbleOverlay.StepFrothMotion(.1);
            Assert.True(b.X > 300); Assert.True(b.FrothShown < 1);
        }
        finally
        {
            BubbleOverlay.Stop(); host.Close(); stage.Pool.Reset(); stage.PoolOn = oldPoolOn; stage.DragsBubbles = oldDrag;
            BubbleOverlay.FrothPointer = oldPointer; BubbleOverlay.FrothEffectOn = oldEffect;
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks;
        }
    });

    [Fact]
    public void Stage_sweep_pops_nearby_bubbles_and_panic_stops_it() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEffect = BubbleOverlay.FrothEffectOn;
        var oldChecks = BubbleOverlay.SkipPlatformChecksForTests;
        var host = new Window();
        try
        {
            BubbleOverlay.SkipPlatformChecksForTests = true; BubbleOverlay.FrothEffectOn = _ => true;
            host.Show(); BubbleOverlay.Start(host);
            BubbleOverlay.Field.Bubbles.Clear();
            var near = new AmbientBubble { X = 100, Y = 100, Size = 100 };
            var far = new AmbientBubble { X = 600, Y = 600, Size = 100 };
            BubbleOverlay.Field.Bubbles.AddRange(new[] { near, far });
            var c = BubbleOverlay.CenterPx(near);
            SuperStageHost.Stage.Sweep(c.X, c.Y, c.X, c.Y);
            Assert.True(near.Popping); Assert.False(far.Popping);
            PanicSurfaces.All.Single(s => s.Id == "bubble-host").Stop(null);
            Assert.Empty(BubbleOverlay.Field.Bubbles); Assert.Equal(0, BubbleOverlay.WindowCount);
            BubbleOverlay.Field.Bubbles.Add(far);
            var f = BubbleOverlay.CenterPx(far);
            SuperStageHost.Stage.Sweep(f.X, f.Y, f.X, f.Y);
            Assert.False(far.Popping);
        }
        finally
        {
            BubbleOverlay.Stop(); host.Close(); BubbleOverlay.FrothEffectOn = oldEffect;
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks;
        }
    });
}
