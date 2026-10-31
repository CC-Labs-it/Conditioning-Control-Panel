using ConditioningControlPanel.Services.AIService;
using Xunit;
using static ConditioningControlPanel.Services.AIService.LocalAiHardwareRules;

namespace CCP.Core.Tests;

/// <summary>
/// The local AI slowdown card (owner, 2026-10-14: mid-range PCs slowed down after installing
/// Ollama with no warning). The verdict decides only what the card says; it never blocks.
/// </summary>
public class LocalAiHardwareRulesTests
{
    [Theory]
    [InlineData(32L, 12L, Fit.Ok)]
    [InlineData(16L, 8L, Fit.Ok)]
    [InlineData(8L, 8L, Fit.Below)]     // too little memory
    [InlineData(32L, 4L, Fit.Below)]    // too little graphics memory
    [InlineData(32L, 0L, Fit.Ok)]       // unknown graphics memory never counts against the PC
    [InlineData(0L, 0L, Fit.Unknown)]
    [InlineData(0L, 2L, Fit.Below)]
    public void Judge_ComparesWithTheRecommendation(long ramGb, long vramGb, Fit expected)
        => Assert.Equal(expected, Judge(ramGb * Gb, vramGb * Gb));

    [Fact]
    public void Judge_ToleratesWhatWindowsReportsForA16GbMachine()
    {
        // Windows reserves a slice: a 16 GB machine reads about 15.8 GB, an 8 GB card about 7.9.
        Assert.Equal(Fit.Ok, Judge((long)(15.8 * Gb), (long)(7.9 * Gb)));
    }

    [Theory]
    [InlineData(17_000_000_000L, "16 GB")]
    [InlineData(0L, "?")]
    [InlineData(-1L, "?")]
    public void FormatGb_RoundsToWholeGigabytes(long bytes, string expected)
        => Assert.Equal(expected, FormatGb(bytes));

}
