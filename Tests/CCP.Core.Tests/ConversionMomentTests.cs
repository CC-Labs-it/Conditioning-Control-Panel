using ConditioningControlPanel.Services.Startup.Conversion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ConversionMomentTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void TasteLineOnlyAtCompletedFreeLiveEnd(bool completed, bool live, bool premium, bool expected)
        => Assert.Equal(expected, SessionRecapConversion.SessionTasteLine(completed, live, premium));
}
