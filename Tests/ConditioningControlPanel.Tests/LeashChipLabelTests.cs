using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Chaster chips on the holder card read "+15m" / "+1h", not "+1:00:00", so the row keeps room for
/// its name (owner screenshot 2026-10-17: the Chaster time row showed no name at all).
/// </summary>
public class LeashChipLabelTests
{
    [Theory]
    [InlineData(15 * 60, "15m")]
    [InlineData(30 * 60, "30m")]
    [InlineData(60 * 60, "1h")]
    [InlineData(90 * 60, "1h 30m")]
    [InlineData(-30 * 60, "30m")]
    public void Span_IsShort(int seconds, string expected) => Assert.Equal(expected, LeashUiRules.Span(seconds));

    [Fact]
    public void ChasterChips_UseTheShortSpan()
    {
        Assert.Equal("+15m", LeashUiRules.SizeText(PunishKind.Chaster, 15 * 60));
        Assert.Equal("+1h", LeashUiRules.SizeText(PunishKind.Chaster, 60 * 60));
        Assert.Equal("10", LeashUiRules.SizeText(PunishKind.Lines, 10));
    }
}
