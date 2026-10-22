using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1457: Takeover opened a HypnoTube video that is VIP-only ("You must be a VIP
/// member to view this video"). Every built-in pool was checked on 2026-10-17, one request per
/// link; these were VIP-only or 404 and were dropped. A built-in pool must stay playable for a
/// free HypnoTube visitor, so none of them may come back.</summary>
public class HypnotubeDeadLinkTests
{
    private static readonly string[] Dropped =
    {
        // VIP-only
        "hypno-trance-127460", "deep-acceptance-113157", "anal-dream-sissygaljasmine-edition-90388",
        "anal-slut-trainer-101540", "eat-your-cum-116026",
        // 404
        "car-cleaning-cuck-audio-132494", "cuckold-sph-you-sleep-on-the-floor-132500", "bbc-barbie-132233",
        "bubblehead-forever-iplaywithdolls-79880", "girly-thoughts-vertical-loop-118644",
        "rubberdoll-obedience-trainer-by-hypno-authority-87892", "the-rise-of-the-drones-32786",
    };

    private static readonly string[] PoolFiles =
    {
        Path.Combine("Models", "BuiltInMods.cs"),
        Path.Combine("AvatarTube", "AvatarTubeWindow.Speech.cs"),
        Path.Combine("Services", "BambiSprite.cs"),
        Path.Combine("Services", "HypnotubeDefaultLinks.cs"),   // CCP.Core: the default pools + known titles moved here
    };

    [Fact]
    public void BuiltInPools_CarryNoVipOrDeadLink()
    {
        foreach (var rel in PoolFiles)
        {
            var src = SourceRoots.ReadProductFile(rel);
            foreach (var slug in Dropped)
                Assert.DoesNotContain("hypnotube.com/video/" + slug + ".html", src);
        }
    }

    [Fact]
    public void LockedMod_StillHasAPool()
    {
        var links = ConditioningControlPanel.Models.BuiltInMods.Locked.Browser?.DefaultVideoLinks;
        Assert.NotNull(links);
        Assert.True(links!.Count >= 10);
    }
}
