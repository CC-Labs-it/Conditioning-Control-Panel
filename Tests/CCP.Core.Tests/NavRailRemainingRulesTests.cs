using System;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class NavRailRemainingRulesTests
{
    [Fact]
    public void ArtWashUsesCurrentWaveElevenAlphasForEverySection()
    {
        foreach (var section in NavSections.Order)
        {
            var hue = 0xFF000000 | NavStripTable.AccentRgb(section.Key);
            Assert.Equal(0x0D, NavStripPaint.A(NavRailPaintRules.ArtTint(hue, true)));
            Assert.Equal(0x00, NavStripPaint.A(NavRailPaintRules.ArtTint(hue, false)));
            Assert.Equal(hue & 0xFFFFFF, NavRailPaintRules.ArtTint(hue, true) & 0xFFFFFF);
            Assert.Equal(hue & 0xFFFFFF, NavRailPaintRules.ArtTint(hue, false) & 0xFFFFFF);
        }
    }

    [Theory]
    [InlineData(null, "renamed", "translated")]
    [InlineData("", "renamed", "translated")]
    [InlineData("original", "", "translated")]
    [InlineData("original", "original", "translated")]
    [InlineData("original", "renamed", "renamed")]
    public void OnlyChangedNonemptyEnglishModTextOverridesLocalization(string? english, string replacement, string expected)
        => Assert.Equal(expected, ModAwareLabelRules.Resolve("translated", english, _ => replacement));

    [Fact]
    public void NoModRetainsTheLocalizedLabel()
        => Assert.Equal("übersetzt", ModAwareLabelRules.Resolve("übersetzt", "English", null));

    [Fact]
    public void EverySectionRingKeepsItsHueAndBevelReversesAtSelection()
    {
        foreach (var section in NavSections.Order)
        {
            uint hue = 0xFF000000 | NavStripTable.AccentRgb(section.Key);
            var vivid = NavRailPaintRules.Vivid(hue);
            var (originalH, _, originalL) = NavStripPaint.ToHsl(hue);
            var (vividH, vividS, vividL) = NavStripPaint.ToHsl(vivid);
            Assert.True(Math.Abs(originalH - vividH) < 1);
            Assert.True(vividS >= .79);
            Assert.True(vividL <= Math.Min(originalL, .58) + .01);
            var idle = NavRailPaintRules.RingStops(hue, false, false);
            var selected = NavRailPaintRules.RingStops(hue, true, false);
            Assert.All(idle, stop => Assert.Equal(0xCC, NavStripPaint.A(stop.Color)));
            Assert.All(NavRailPaintRules.RingStops(hue, false, true), stop => Assert.Equal(0xF2, NavStripPaint.A(stop.Color)));
            Assert.All(selected, stop => Assert.Equal(0xFF, NavStripPaint.A(stop.Color)));
            Assert.Equal(idle[0].Color & 0xFFFFFF, selected[2].Color & 0xFFFFFF);
            Assert.Equal(idle[2].Color & 0xFFFFFF, selected[0].Color & 0xFFFFFF);
            Assert.Equal(new[] { 0.0, .42, 1.0 }, idle.Select(stop => stop.Offset));
            Assert.Equal(new[] { 0.0, .5, 1.0 }, selected.Select(stop => stop.Offset));
        }
        Assert.Equal(3, NavRailPaintRules.RingThickness(false));
        Assert.Equal(3.5, NavRailPaintRules.RingThickness(true));
    }
}
