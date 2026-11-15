using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Declutter wave 1 (2026-11-15), twin of WPF StudioRackRulesTests: on the Studio rack, SELECTED
/// and ON are two signals. On is a filled dot and a full-strength label, off a hollow ring and a
/// dimmed label. Group headers count what is on.
/// </summary>
public sealed class StudioRackRulesTests
{
    [Fact]
    public void OnIsAFilledDotAndAFullLabel_OffIsARingAndADimmedLabel()
    {
        Assert.True(StudioRackRules.DotIsFilled(true));
        Assert.False(StudioRackRules.DotIsFilled(false));
        Assert.Equal(1.0, StudioRackRules.LabelOpacity(true));
        Assert.Equal(StudioRackRules.OffLabelOpacity, StudioRackRules.LabelOpacity(false));
        Assert.InRange(StudioRackRules.OffLabelOpacity, 0.5, 0.6);
        Assert.Equal(1.5, StudioRackRules.OffRingThickness);
    }

    [Fact]
    public void AnUnknownStateReadsAsOff()
    {
        Assert.False(StudioRackRules.IsOn(null));
        Assert.False(StudioRackRules.DotIsFilled(null));
        Assert.Equal(StudioRackRules.OffLabelOpacity, StudioRackRules.LabelOpacity(null));
    }

    [Fact]
    public void AGroupCountsOnlyTheRowsThatHaveASwitch()
    {
        var rows = new (bool, bool?)[]
        {
            (true, true), (true, false), (true, null), (true, true),
            (false, null),   // Visuals: no master switch, counted on neither side
        };
        Assert.Equal((2, 4), StudioRackRules.Count(rows));
        Assert.Equal("2 of 4 on", StudioRackRules.CountText("{0} of {1} on", 2, 4));
    }

    [Fact]
    public void AGroupWithNothingToCountShowsNoNumber()
    {
        Assert.Equal((0, 0), StudioRackRules.Count(new (bool, bool?)[] { (false, null) }));
        Assert.Null(StudioRackRules.CountText("{0} of {1} on", 0, 0));
        Assert.Null(StudioRackRules.CountText("", 1, 2));
        Assert.Null(StudioRackRules.CountText("{0} of {5} on", 1, 2));
    }

    [Fact]
    public void TheSelectedFillIsNotTheRackGround()
        => Assert.NotEqual(StudioRackRules.SelectedFillBrushKey, StudioRackRules.RackGroundBrushKey);

    [Fact]
    public void TheCountLineIsInEveryLanguageWithBothNumbers()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            Assert.True(doc.RootElement.TryGetProperty(StudioRackRules.GroupCountKey, out var v),
                $"{Path.GetFileName(f)} has no {StudioRackRules.GroupCountKey}");
            var text = v.GetString() ?? "";
            Assert.Contains("{0}", text);
            Assert.Contains("{1}", text);
            Assert.DoesNotContain("!", text);
            Assert.DoesNotContain("—", text);
            var shown = StudioRackRules.CountText(text, 3, 7);
            Assert.NotNull(shown);
            Assert.Contains("3", shown);
            Assert.Contains("7", shown);
        }
    }
}
