using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// ccp-bugs #1627: the Linux head only offers a release that carries the linux-x64 tarball. The list
/// below has the shape of GET /releases on 2026-11-15: 7.2.4 is Latest and Windows-only, the Linux
/// builds are the 7.2.2 beta pre-releases.
/// </summary>
public sealed class LinuxReleasePickTests
{
    private const string Page = ReleaseLinks.ReleasesListUrl + "/tag/";

    private static string Release(string tag, bool prerelease, params string[] assets) => Release(tag, prerelease, false, Page + tag, assets);

    private static string Release(string tag, bool prerelease, bool draft, string htmlUrl, params string[] assets) =>
        $"{{\"tag_name\":\"{tag}\",\"draft\":{(draft ? "true" : "false")},\"prerelease\":{(prerelease ? "true" : "false")},"
        + $"\"html_url\":\"{htmlUrl}\",\"body\":\"notes {tag}\",\"assets\":["
        + string.Join(",", assets.Select((a, i) => $"{{\"name\":\"{a}\",\"size\":{100 + i},\"browser_download_url\":\"https://example.invalid/{a}\"}}"))
        + "]}";

    private static string List(params string[] releases) => "[" + string.Join(",", releases) + "]";

    // GET /releases as it stood when #1627 was filed (newest first).
    private static readonly string Today = List(
        Release("v7.2.4", false, "ConditioningControlPanel-7.2.4-Setup.exe"),
        Release("v7.2.2-linux-beta2", true, "ConditioningControlPanel-7.2.2-linux-x64.tar.gz", "ConditioningControlPanel-7.2.2-linux-x64.tar.gz.sha256"),
        Release("v7.2.3", true, "ConditioningControlPanel-7.2.3-Setup.exe"),
        Release("v7.2.2-linux-beta1", true, "ConditioningControlPanel-7.2.2-linux-x64.tar.gz", "ConditioningControlPanel-7.2.2-linux-x64.tar.gz.sha256"),
        Release("v7.2.2", false, "ConditioningControlPanel-7.2.2-Setup.exe"),
        Release("rain-assets-v1", true, "rain-assets.zip"));

    [Fact]
    public void LatestIsWindowsOnly_TheNewestLinuxBuildIsPickedNotLatest()
    {
        var pick = ReleaseFeed.PickLinuxRelease(Today, includePrereleases: true);
        Assert.NotNull(pick);
        Assert.Equal(new Version(7, 2, 2), pick!.Version);   // never 7.2.4: it has no tarball
        Assert.Equal("v7.2.2-linux-beta2", pick.Tag);       // same version as beta 1: the newer listing wins
        Assert.Equal(Page + "v7.2.2-linux-beta2", pick.PageUrl);
        Assert.Equal(100, pick.Size);                       // the tarball, not the .sha256
        Assert.Equal("notes v7.2.2-linux-beta2", pick.Notes);

        // A stable install never takes a pre-release, and every Linux build so far is one.
        Assert.Null(ReleaseFeed.PickLinuxRelease(Today, includePrereleases: false));
    }

    [Fact]
    public void LatestHasTheTarball_ItIsPickedForBetaAndStable()
    {
        var json = List(
            Release("v7.3.0", false, "ConditioningControlPanel-7.3.0-Setup.exe", "ConditioningControlPanel-7.3.0-linux-x64.tar.gz"),
            Release("v7.2.2-linux-beta2", true, "ConditioningControlPanel-7.2.2-linux-x64.tar.gz"));
        foreach (var beta in new[] { true, false })
        {
            var pick = ReleaseFeed.PickLinuxRelease(json, beta)!;
            Assert.Equal(new Version(7, 3, 0), pick.Version);
            Assert.Equal(Page + "v7.3.0", pick.PageUrl);
            Assert.Equal(101, pick.Size);
        }

        // A newer Linux beta beats an older stable one only on a beta install.
        var both = List(
            Release("v7.4.0-linux-beta1", true, "ConditioningControlPanel-7.4.0-linux-x64.tar.gz"),
            Release("v7.3.0", false, "ConditioningControlPanel-7.3.0-linux-x64.tar.gz"));
        Assert.Equal(new Version(7, 4, 0), ReleaseFeed.PickLinuxRelease(both, true)!.Version);
        Assert.Equal(new Version(7, 3, 0), ReleaseFeed.PickLinuxRelease(both, false)!.Version);
    }

    [Fact]
    public void NoLinuxReleaseNewerThanTheRunningOne_NothingIsOffered()
    {
        // What the head does with the pick (AppUpdater.FetchLinuxAsync): offer only a newer version.
        static bool Offered(string json, string running) =>
            ReleaseFeed.PickLinuxRelease(json, true) is { } p && p.Version > Version.Parse(running);

        Assert.False(Offered(Today, "7.2.2"));   // the beta 2 install the member ran: no offer at all
        Assert.True(Offered(Today, "7.2.1"));
        Assert.False(Offered(List(Release("v7.2.4", false, "ConditioningControlPanel-7.2.4-Setup.exe")), "7.2.2"));
        Assert.Null(ReleaseFeed.PickLinuxRelease("[]", true));
    }

    [Fact]
    public void DraftsForeignPagesAndUnversionedTarballs()
    {
        // A draft never counts, even when it is newer.
        var draft = List(
            Release("v7.5.0", false, true, Page + "v7.5.0", "ConditioningControlPanel-7.5.0-linux-x64.tar.gz"),
            Release("v7.2.2-linux-beta2", true, "ConditioningControlPanel-7.2.2-linux-x64.tar.gz"));
        Assert.Equal(new Version(7, 2, 2), ReleaseFeed.PickLinuxRelease(draft, true)!.Version);

        // A page outside our releases list is never opened: it is rebuilt from the tag.
        var foreign = List(Release("v7.3.0", false, false, "https://evil.example/x", "ConditioningControlPanel-7.3.0-linux-x64.tar.gz"));
        Assert.Equal(Page + "v7.3.0", ReleaseFeed.PickLinuxRelease(foreign, false)!.PageUrl);

        // A tarball without a version in its name falls back to the tag's leading numbers.
        var bare = List(Release("v7.3.1-linux", false, "ccp-linux-x64.tar.gz"));
        Assert.Equal(new Version(7, 3, 1), ReleaseFeed.PickLinuxRelease(bare, false)!.Version);
    }
}
