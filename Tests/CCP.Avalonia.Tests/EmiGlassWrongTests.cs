using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassWrongTests
{
    // WPF EmiChannels.Wrong.cs:53-55, 151-183, 265-341.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task FramesFlashShowWrongnessCollapseAndHoldDead(bool negative) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var host = new Canvas { Width = 152, Height = 137 };
        var painter = new WrongPainter(152, 137, new Random(21), negative);
        painter.Attach(host);
        var dead = Assert.IsType<Rectangle>(host.Children[^3]);
        var tear = Assert.IsType<Rectangle>(host.Children[^2]);
        var flash = Assert.IsType<Rectangle>(host.Children[^1]);
        painter.Tick(0);
        Assert.Equal(1, flash.Opacity);
        Assert.Equal(0, dead.Opacity);
        painter.Tick(45);
        Assert.Equal(.5, flash.Opacity, 5);
        painter.Tick(500);
        Assert.Equal(0, flash.Opacity);
        Assert.Equal(0, dead.Opacity);
        if (negative)
            Assert.Equal(2, host.Children.OfType<TextBlock>().Count(t => t.Text == "0_0"));
        else
        {
            var word = Assert.Single(host.Children.OfType<TextBlock>());
            Assert.Contains(word.Text, new[] { "soon", "hi", "again" });
            Assert.Equal(.35, word.Opacity, 5);
            Assert.Equal(161 + 5, host.Children.OfType<Rectangle>().Count());
        }
        painter.Tick(1000);
        Assert.Equal(.25, dead.Opacity, 5);
        Assert.Equal(.9, tear.Opacity, 5);
        Assert.True(tear.Height < 137);
        painter.Tick(1100);
        Assert.True(tear.Width < 152);
        for (int ms = 1101; ms <= 1150; ms++) painter.Tick(ms);
        Assert.Equal(1, dead.Opacity);
        for (int ms = 1151; ms <= 1400; ms++) painter.Tick(ms);
        Assert.Equal(1, dead.Opacity);
        Assert.Equal(0, tear.Opacity);
        Assert.Equal(0, flash.Opacity);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NegativeFramesRenderFlashDropoutCollapseAndDead() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var host = new Canvas { Width = 152, Height = 137, ClipToBounds = true };
        var painter = new WrongPainter(152, 137, new HalfRandom(), true);
        painter.Attach(host);
        byte[] Frame(int ms)
        {
            painter.Tick(ms);
            host.Measure(new global::Avalonia.Size(152, 137));
            host.Arrange(new global::Avalonia.Rect(0, 0, 152, 137));
            using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize(152, 137));
            bitmap.Render(host);
            var pixels = new byte[152 * 137 * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bitmap.CopyPixels(new global::Avalonia.PixelRect(0, 0, 152, 137), handle.AddrOfPinnedObject(), pixels.Length, 152 * 4); }
            finally { handle.Free(); }
            return pixels;
        }
        var flash = Frame(0);
        var core = Frame(200);
        var dropout = Frame(330);
        var returned = Frame(500);
        var collapse = Frame(1000);
        var dead = Frame(1150);
        Assert.NotEqual(flash, core);
        Assert.NotEqual(core, dropout);
        Assert.NotEqual(dropout, returned);
        Assert.NotEqual(collapse, returned);
        Assert.Equal(dropout, dead);
        return Task.CompletedTask;
    });

    private sealed class HalfRandom : Random
    {
        public override double NextDouble() => .5;
    }
}
