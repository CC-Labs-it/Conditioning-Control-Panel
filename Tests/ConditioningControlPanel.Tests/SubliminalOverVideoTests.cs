using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// ccp-bugs #1422: words sent by Remote Control were unreadable during a video, because the
    /// card rode the compositor host and that host is pinned below the video window.
    /// </summary>
    public class SubliminalOverVideoTests
    {
        private static readonly IntPtr Card = new(10);
        private static readonly IntPtr Video1 = new(20);
        private static readonly IntPtr Video2 = new(21);
        private static readonly IntPtr LockCard = new(30);
        private static readonly IntPtr Target = new(31);
        private static readonly IntPtr Host = new(40);
        private static readonly IntPtr Mon1 = new(1);
        private static readonly IntPtr Mon2 = new(2);

        private static readonly (IntPtr, IntPtr)[] OneVideo = { (Video1, Mon1) };

        [Fact]
        public void ACardLeavesTheCompositorHostWhileAVideoWindowIsUp()
        {
            Assert.True(SubliminalOverVideo.UsesCompositorCard(compositorOn: true, stealsFocus: false, videoWindowUp: false));
            Assert.False(SubliminalOverVideo.UsesCompositorCard(compositorOn: true, stealsFocus: false, videoWindowUp: true));
        }

        [Fact]
        public void TheOlderCompositorRulesStillHold()
        {
            Assert.False(SubliminalOverVideo.UsesCompositorCard(compositorOn: false, stealsFocus: false, videoWindowUp: false));
            Assert.False(SubliminalOverVideo.UsesCompositorCard(compositorOn: true, stealsFocus: true, videoWindowUp: false));
        }

        [Fact]
        public void WithNoVideoTheCardGoesToTheTopOfTheBandAsBefore()
        {
            var slot = SubliminalOverVideo.ResolveSlot(new[] { LockCard, Card },
                Array.Empty<(IntPtr, IntPtr)>(), Card, Mon1);
            Assert.Equal(SubliminalOverVideo.SlotKind.TopOfBand, slot.Kind);
        }

        [Fact]
        public void ACardBuriedUnderTheVideoIsLiftedDirectlyAboveIt()
        {
            // The reported shape: the video is in front, the card somewhere under it.
            var slot = SubliminalOverVideo.ResolveSlot(new[] { Video1, Host, Card }, OneVideo, Card, Mon1);
            Assert.Equal(SubliminalOverVideo.SlotKind.TopOfBand, slot.Kind);
        }

        [Fact]
        public void ALockCardAboveTheVideoStaysAboveTheWords()
        {
            var slot = SubliminalOverVideo.ResolveSlot(new[] { LockCard, Video1, Host, Card }, OneVideo, Card, Mon1);
            Assert.Equal(SubliminalOverVideo.SlotKind.Below, slot.Kind);
            Assert.Equal(LockCard, slot.InsertAfter);
        }

        [Fact]
        public void TheCardGoesUnderTheNearestWindowAboveTheVideoNotTheFrontOne()
        {
            var slot = SubliminalOverVideo.ResolveSlot(new[] { LockCard, Target, Video1, Card }, OneVideo, Card, Mon1);
            Assert.Equal(SubliminalOverVideo.SlotKind.Below, slot.Kind);
            Assert.Equal(Target, slot.InsertAfter);
        }

        [Fact]
        public void ACardAlreadyDirectlyAboveTheVideoIsLeftAlone()
        {
            var slot = SubliminalOverVideo.ResolveSlot(new[] { LockCard, Card, Video1 }, OneVideo, Card, Mon1);
            Assert.Equal(SubliminalOverVideo.SlotKind.Keep, slot.Kind);
        }

        [Fact]
        public void ACardThatSatAboveALockCardComesBackDownUnderIt()
        {
            var slot = SubliminalOverVideo.ResolveSlot(new[] { Card, LockCard, Video1 }, OneVideo, Card, Mon1);
            Assert.Equal(SubliminalOverVideo.SlotKind.Below, slot.Kind);
            Assert.Equal(LockCard, slot.InsertAfter);
        }

        [Fact]
        public void EachScreenAnchorsToItsOwnVideoWindow()
        {
            var videos = new[] { (Video1, Mon1), (Video2, Mon2) };
            // Screen 2's video is deeper in the band than screen 1's; a lock card sits between.
            var band = new[] { Video1, LockCard, Video2, Card };
            var slot = SubliminalOverVideo.ResolveSlot(band, videos, Card, Mon2);
            Assert.Equal(SubliminalOverVideo.SlotKind.Below, slot.Kind);
            Assert.Equal(LockCard, slot.InsertAfter);
        }

        [Fact]
        public void AScreenWithNoVideoOfItsOwnStillStaysUnderWhatIsAboveTheClip()
        {
            var slot = SubliminalOverVideo.ResolveSlot(new[] { LockCard, Video1, Card }, OneVideo, Card, Mon2);
            Assert.Equal(SubliminalOverVideo.SlotKind.Below, slot.Kind);
            Assert.Equal(LockCard, slot.InsertAfter);
        }

        [Fact]
        public void AVideoHandleThatIsNotInTheBandFallsBackToTheTop()
        {
            var slot = SubliminalOverVideo.ResolveSlot(new List<IntPtr> { LockCard, Card }, OneVideo, Card, Mon1);
            Assert.Equal(SubliminalOverVideo.SlotKind.TopOfBand, slot.Kind);
        }
    }
}
