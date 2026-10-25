using System;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiBookGlyphPaintersTests
{
    [Fact]
    public void EveryCardHasDistinctTransparentRailArtAndNoPainterIsOrphaned()
    {
        Assert.Equal(EmiBookCards.All.Select(c => c.Id).OrderBy(x => x), EmiBookGlyphPainters.Ids.OrderBy(x => x));
        var images = EmiBookGlyphPainters.Ids.Select(id =>
        {
            var canvas = new EmiPixelCanvas(16, 16);
            canvas.Clear(0x12345678); // Drawing clears a reused buffer's old contents.
            Assert.True(EmiBookGlyphPainters.Draw(id, canvas));
            Assert.Contains(0u, canvas.Pixels);
            Assert.Contains(canvas.Pixels, p => p >> 24 == 255);
            Assert.All(canvas.Pixels, p => Assert.True(p == 0 || p >> 24 == 255));
            return string.Join(",", canvas.Pixels);
        }).ToArray();
        Assert.Equal(images.Length, images.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PanicKeyHasACreamFacePinkCentreAndTransparentSurround()
    {
        var canvas = new EmiPixelCanvas(16, 16);
        Assert.True(EmiBookGlyphPainters.Draw("the-panic-key", canvas));
        Assert.Equal(0u, canvas.Pixels[0]);
        Assert.Equal(EmiPixelCanvas.Rgb(0xF5, 0xF0, 0xE1), canvas.Pixels[3 * 16 + 2]);
        Assert.Equal(EmiPixelCanvas.Rgb(0xFF, 0x69, 0xB4), canvas.Pixels[5 * 16 + 6]);
        Assert.False(EmiBookGlyphPainters.Draw("missing-card", canvas));
    }
}
