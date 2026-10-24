using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// ccp-bugs #1201: a chaos video request carries a token taken at detonation, a run exit cancels
/// through it, and every step between the request and the first frame abandons a cancelled one.
/// The rule lives in Core (<see cref="VideoTriggerRules.ShouldAbandonVideoRequest"/>); the
/// queue-parked case end to end stays with the WPF head in ChaosVideoStartWindowTests.
/// </summary>
public class VideoRequestCancelTests
{
    /// <summary>
    /// Armed but not yet playing, and the run ends: the request is cancelled, so no video opens
    /// over the lobby. This is the case the old teardown missed entirely.
    /// </summary>
    [Fact]
    public void ArmedButNotPlayingYet_TheRequestIsCancelled()
    {
        int token = 1;                 // the detonation took a token
        int cancelledThrough = token;  // the run ended and swept through it

        Assert.True(VideoTriggerRules.ShouldAbandonVideoRequest(token, cancelledThrough));
    }

    /// <summary>
    /// The ownership half, restated on the new seam: a video nobody in the descent asked for
    /// carries no token, so a run exit cannot cancel a user's own mandatory or session video -
    /// however many chaos requests have been cancelled around it.
    /// </summary>
    [Fact]
    public void AVideoTheUserAskedFor_IsNeverCancelled()
    {
        Assert.False(VideoTriggerRules.ShouldAbandonVideoRequest(requestToken: 0, cancelledThrough: 0));
        Assert.False(VideoTriggerRules.ShouldAbandonVideoRequest(requestToken: 0, cancelledThrough: 47));
    }

    /// <summary>A video the NEXT run asks for is newer than the cancel and must still play, or one
    /// quit would poison every descent after it.</summary>
    [Fact]
    public void ARequestMadeAfterTheCancel_StillPlays()
    {
        Assert.False(VideoTriggerRules.ShouldAbandonVideoRequest(requestToken: 2, cancelledThrough: 1));
    }

    /// <summary>Several bubbles can have popped in one run; ending it takes them all, not just the
    /// last one.</summary>
    [Fact]
    public void EveryOutstandingRequestOfTheRun_GoesTogether()
    {
        Assert.True(VideoTriggerRules.ShouldAbandonVideoRequest(requestToken: 1, cancelledThrough: 3));
        Assert.True(VideoTriggerRules.ShouldAbandonVideoRequest(requestToken: 2, cancelledThrough: 3));
        Assert.True(VideoTriggerRules.ShouldAbandonVideoRequest(requestToken: 3, cancelledThrough: 3));
    }
}
