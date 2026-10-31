using ConditioningControlPanel.Services.PieceByPiece;
using Xunit;
namespace CCP.Core.Tests;
public class PbpMediaAvailabilityTests
{
    [Theory]
    [InlineData(false, "off", false)]
    [InlineData(false, "empty", false)]
    [InlineData(false, "loading", true)]
    [InlineData(false, "ready", true)]
    [InlineData(false, "error", true)]
    [InlineData(true, "off", true)]
    [InlineData(true, "empty", true)]
    public void DryOnlineAnswersOnlyRemoveTheOnlineSource(bool local, string state, bool expected)
        => Assert.Equal(expected, PbpMediaRules.MediaAvailable(local, new[] { "valid" }, state));
}
