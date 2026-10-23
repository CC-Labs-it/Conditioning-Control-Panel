using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
using IType = ConditioningControlPanel.Services.InteractionQueueService.InteractionType;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1201, the half the first fix did not reach: the START window.
///
/// A video bubble arms the ownership cap the instant it detonates, but the video itself is only
/// REQUESTED then. Choosing a clip runs off the UI thread and can take seconds (a content-pack
/// decrypt, a full-library refill), the freeze delay adds another 800ms, and the request can be
/// parked in the InteractionQueue behind a bubble count or a lock card for longer still. So a run
/// ending a second after the pop asked "is a video playing", was told no, tore nothing down, and
/// the tape opened over the results card and the lobby a moment later - exactly the reported
/// symptom, on a window of at least 800ms that anyone quitting right after a pop lands in.
///
/// The request now carries a token taken at detonation. A run exit cancels through it, and every
/// step between the request and the first frame abandons a cancelled one. The pure cancel rule is
/// pinned in CCP.Core.Tests (VideoRequestCancelTests); these pin the queue-parked case end to end
/// on the real queue and the tape-on-screen rule.
/// </summary>
[Collection(InteractionQueueSchedulerCollection.Name)]
public class ChaosVideoStartWindowTests : IDisposable
{
    private readonly Action<Action> _originalScheduler;

    public ChaosVideoStartWindowTests()
    {
        _originalScheduler = InteractionQueueService.TriggerScheduler;
        // Run a dequeued replay inline rather than through the (absent) Dispatcher.
        InteractionQueueService.TriggerScheduler = a => a();
    }

    public void Dispose() => InteractionQueueService.TriggerScheduler = _originalScheduler;

    /// <summary>
    /// The queue path, end to end on the real InteractionQueueService. A chaos video that arrives
    /// while a bubble count owns the screen is parked with a replay callback; the run then ends,
    /// and the card closes minutes later. The replay must find itself obsolete and hand the slot
    /// straight back rather than playing a video for a descent that is over.
    /// </summary>
    [Fact]
    public void AParkedChaosVideo_IsNotReplayedAfterTheRunEnds()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.BubbleCount, () => { });   // a bubble count owns the screen

        const int token = 1;
        int cancelledThrough = 0;
        int played = 0;

        // What VideoService parks: the same TriggerVideo call, carrying the token.
        q.TryStart(IType.Video, () =>
        {
            if (VideoTriggerRules.ShouldAbandonVideoRequest(token, cancelledThrough))
            {
                q.CompleteIfCurrent(IType.Video);
                return;
            }
            played++;
        }, queue: true);

        Assert.Equal(1, q.QueuedCount);

        cancelledThrough = token;       // the run ends: StopChaosOwnedVideo cancels the request
        q.Complete(IType.BubbleCount);  // the card closes and the queue replays the parked video

        Assert.Equal(0, played);
        Assert.False(q.IsBusy);         // and the slot is not left claimed for the 5-minute stuck window
    }

    /// <summary>The same park, with no run behind it: a video the user earned outside a descent is
    /// still replayed when the card closes.</summary>
    [Fact]
    public void AParkedVideoWithNoRunBehindIt_StillPlays()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.BubbleCount, () => { });

        int cancelledThrough = 9;   // descents have come and gone
        int played = 0;

        q.TryStart(IType.Video, () =>
        {
            if (VideoTriggerRules.ShouldAbandonVideoRequest(0, cancelledThrough)) return;
            played++;
        }, queue: true);

        q.Complete(IType.BubbleCount);

        Assert.Equal(1, played);
    }

    /// <summary>A tape whose windows are up but whose player has not started is still something to
    /// close on the way out.</summary>
    [Fact]
    public void WindowsUpWithoutPlayback_IsStillOnScreen()
    {
        Assert.True(ChaosModeService.ChaosTapeIsOnScreen(videoPlaying: false, hasOpenWindows: true, cleaningUp: false));
        Assert.True(ChaosModeService.ChaosTapeIsOnScreen(videoPlaying: true, hasOpenWindows: false, cleaningUp: false));
    }

    /// <summary>A teardown already in flight is not "on screen": ForceCleanup is doing the work,
    /// and re-entering it pumps the dispatcher inside its own pump.</summary>
    [Fact]
    public void ATeardownAlreadyRunning_IsNotReEntered()
    {
        Assert.False(ChaosModeService.ChaosTapeIsOnScreen(videoPlaying: false, hasOpenWindows: true, cleaningUp: true));
    }

    /// <summary>Nothing up at all: no ForceCleanup, so a run exit never yanks the audio duck or the
    /// queue slot out from under whatever else is on screen.</summary>
    [Fact]
    public void NothingUp_IsNotOnScreen()
    {
        Assert.False(ChaosModeService.ChaosTapeIsOnScreen(videoPlaying: false, hasOpenWindows: false, cleaningUp: false));
    }
}
