using System;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The persistent speak cue's rules, against WPF Overlays/SpeakCueOverlay.cs.</summary>
public class SpeakCueStateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankText_IsIgnored(string? text)
    {
        var s = new SpeakCueState();
        Assert.False(s.Show(text));
        Assert.False(s.Visible);
        Assert.Null(s.Text);
    }

    [Fact]
    public void Show_ReplacesText_HideFades_FadedOutClears()
    {
        var s = new SpeakCueState();
        Assert.False(s.Hide());                 // nothing showing
        Assert.True(s.Show("Say YES"));
        Assert.True(s.Show("Say PLEASE"));
        Assert.Equal("Say PLEASE", s.Text);
        Assert.True(s.Hide());
        Assert.False(s.Hide());
        Assert.Equal("Say PLEASE", s.Text);     // still drawn while it fades
        Assert.True(s.FadedOut());
        Assert.Null(s.Text);
    }

    [Fact]
    public void ShowDuringFadeOut_KeepsTheCue()
    {
        var s = new SpeakCueState();
        s.Show("Say YES");
        s.Hide();
        s.Show("Say YES");
        Assert.False(s.FadedOut());
        Assert.True(s.Visible);
        Assert.Equal("Say YES", s.Text);
    }

    [Fact]
    public void Fade_IsLinearOver180ms_ToPeak092()
    {
        Assert.Equal(180, SpeakCueState.FadeMs);
        Assert.Equal(0.92, SpeakCueState.PeakOpacity);
        Assert.Equal(0, SpeakCueState.FadeAt(0, 0.92, TimeSpan.Zero));
        Assert.Equal(0.46, SpeakCueState.FadeAt(0, 0.92, TimeSpan.FromMilliseconds(90)), 6);
        Assert.Equal(0.92, SpeakCueState.FadeAt(0, 0.92, TimeSpan.FromMilliseconds(180)));
        Assert.Equal(0, SpeakCueState.FadeAt(0.92, 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0.92, SpeakCueState.FadeAt(0.92, 0, TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void Anchor_IsWorkAreaRelativeToVirtualScreen_Plus120()
    {
        // Primary work area right of a left monitor, with a 30 DIP top panel.
        Assert.Equal((1920.0, 150.0), SpeakCueState.Anchor(1920, 30, 0, 0));
        Assert.Equal((1920.0, 120.0), SpeakCueState.Anchor(0, 0, -1920, 0));
        Assert.Equal((0.0, 120.0), SpeakCueState.Anchor(0, 0, 100, 50));    // never negative
    }
}
