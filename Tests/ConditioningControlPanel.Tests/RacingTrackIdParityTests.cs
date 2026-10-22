using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Core RacingAccess carries its own copy of the rt.original.NN format (it cannot see
/// the head's PrizeGrants); this pins the two together so the door never asks for a wrong id.</summary>
public class RacingTrackIdParityTests
{
    [Fact]
    public void CoreTrackIdsMatchPrizeGrants()
    {
        Assert.Equal(PrizeGrants.RacingTrackMax, RacingAccess.RacingTrackMax);
        for (int n = 0; n <= PrizeGrants.RacingTrackMax; n++)
            Assert.Equal(PrizeGrants.RacingTrack(n), RacingAccess.RacingTrack(n));
    }
}
