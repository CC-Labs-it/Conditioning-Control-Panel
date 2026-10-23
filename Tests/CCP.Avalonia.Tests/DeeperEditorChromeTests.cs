using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperEditorChromeTests
{
    [Theory]
    [InlineData(255, 105, 180, 0xB469FF, 0x260816)]
    [InlineData(16, 8, 38, 0x260810, 0xFFFFFF)]
    [InlineData(255, 255, 255, 0xFFFFFF, 0x260816)]
    public void NativeCaptionMatchesWpfAccentAndContrast(byte r, byte g, byte b, int caption, int text)
    {
        var calls = new List<(int Attribute, int Value)>();
        WindowChrome.ApplyAttributes(r, g, b, (attribute, value) => calls.Add((attribute, value)));
        Assert.Equal(new[] { (20, 1), (35, caption), (34, caption), (36, text) }, calls);
    }

    [Fact]
    public void PageZoomUsesWpfStepsAndBounds()
    {
        double zoom = 1;
        for (int i = 0; i < 7; i++) zoom = DeeperPageBridge.Next(zoom, 0.10);
        Assert.Equal(1.7, zoom, 9);
        Assert.Equal(0.9, DeeperPageBridge.Next(1, -0.10), 9);
        Assert.Equal(0.25, DeeperPageBridge.Next(0.30, -0.10));
        Assert.Equal(5, DeeperPageBridge.Next(5, 0.10));
    }
}
