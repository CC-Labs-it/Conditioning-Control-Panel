using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class FlashDeliveryTests
{
    [Fact]
    public void StartsAtBubbleAndFinishesAtFlash()
    {
        var target = new Rect(-1200, 200, 600, 400);
        var origin = new Point(-1100, 350);
        var start = FlashDelivery.Sample(origin, 80, target, 0, MotionLevel.Full);
        Assert.Equal(new Rect(-1140, 310, 80, 80), start.Rect);
        var end = FlashDelivery.Sample(origin, 80, target, 1, MotionLevel.Full);
        Assert.Equal(target, end.Rect);
        Assert.Equal(1, end.Alpha);
    }

    [Fact]
    public void ReducedFadesWithoutMovingAndOffCuts()
    {
        var target = new Rect(500, 200, 600, 400);
        var reduced = FlashDelivery.Sample(new Point(800, 400), 80, target, .09, MotionLevel.Reduced);
        Assert.Equal(target, reduced.Rect);
        Assert.Equal(.5, reduced.Alpha, 6);
        var off = FlashDelivery.Sample(new Point(800, 400), 80, target, 0, MotionLevel.Off);
        Assert.Equal(target, off.Rect);
        Assert.Equal(1, off.Alpha);
    }

    // ccp-bugs #1716: the picture a Flash Images bubble delivers was forced Still, so the first
    // flash of the burst never moved. It now rolls its motion and the layer holds it until landing.
    [Fact]
    public void A_delivered_picture_rolls_motion_and_waits_for_the_entrance()
    {
        var root = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (root != null && !System.IO.Directory.Exists(System.IO.Path.Combine(root.FullName, "ConditioningControlPanel", "Resources"))) root = root.Parent;
        Assert.NotNull(root);
        var app = System.IO.Path.Combine(root!.FullName, "ConditioningControlPanel");
        var service = System.IO.File.ReadAllText(System.IO.Path.Combine(app, "Services", "Flash", "FlashService.cs"));
        Assert.DoesNotContain("BubbleOriginPx != null ? FlashMotionStyle.Still", service);
        var layer = System.IO.File.ReadAllText(System.IO.Path.Combine(app, "Services", "Compositor", "FlashLayer.cs"));
        Assert.Contains("item.Motion != null && item.BubbleOriginPx == null", layer);
    }
}
