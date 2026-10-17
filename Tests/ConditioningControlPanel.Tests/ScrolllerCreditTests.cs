using System;
using System.IO;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Video;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "via Scrolller" credit on mandatory videos (owner, 2026-10-16): a corner badge, an end
/// card, and a link to the subreddit that never opens under a lock.
/// </summary>
public class ScrolllerCreditTests
{
    [Fact]
    public void FromId_ReadsTheSubFromAScrolllerEntryId()
    {
        var c = ScrolllerCredit.FromId("scrolller/gifs/25505178");
        Assert.NotNull(c);
        Assert.Equal("gifs", c!.Sub);
        Assert.Equal("https://scrolller.com/r/gifs", c.Link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("local/gifs/1")]
    [InlineData("scrolller/gifs")]
    [InlineData("scrolller/a\"b/1")]
    [InlineData("scrolller/a b/1")]
    [InlineData("scrolller/x/1")]
    public void FromId_RefusesAnythingElse(string? id) => Assert.Null(ScrolllerCredit.FromId(id));

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void Link_NeverOpensUnderALock(bool strict, bool lockdown, bool chaos, bool open)
        => Assert.Equal(open, ScrolllerCredit.CanOpenLink(strict, lockdown, chaos));

    [Fact]
    public void RememberedClip_CarriesThePagePayload()
    {
        var url = "https://v.scrolller.com/test-" + Guid.NewGuid().ToString("N") + ".mp4";
        Assert.Null(ScrolllerCredit.PagePayload(url, true));

        ScrolllerCredit.Remember(new FypAssetManifest.Entry { Id = "scrolller/NSFW_GIF/42", Url = url });

        var o = JObject.FromObject(ScrolllerCredit.PagePayload(url, false)!);
        Assert.Equal("r/NSFW_GIF", (string?)o["sub"]);
        Assert.Equal("scrolller.com/r/NSFW_GIF", (string?)o["site"]);
        Assert.False((bool)o["clickable"]!);
        Assert.Equal(ScrolllerCredit.OutroMs, (int)o["outroMs"]!);
    }

    [Fact]
    public void LocalClip_HasNoCredit()
        => Assert.Null(ScrolllerCredit.PagePayload(@"C:\videos\clip.mp4", true));

    // player.js has no test home, so its half of the contract is asserted on the source.
    [Fact]
    public void PlayerPage_HoldsTheEndCardAndReportsItsOwnPress()
    {
        var js = ReadProduct("ConditioningControlPanel", "Resources", "web", "player", "player.js");
        var html = ReadProduct("ConditioningControlPanel", "Resources", "web", "player", "index.html");
        Assert.Contains("post({ type: 'creditClick' })", js);
        Assert.Contains("case 'creditQueued':", js);
        Assert.Contains("showOutro(c)", js);
        Assert.Contains("clearCredit();", js);   // teardown drops the card, so panic does too
        Assert.Contains("post({ type: 'mediaEnded' })", js);   // host stops judging during the card
    }

    // Review 2026-10-16: a link pressed before a panic must never open in a later run, and nothing
    // may judge the clip during the end card. Asserted on the source (no headless VideoService).
    [Fact]
    public void QueuedLink_OpensOnlyAtThisClipsPictureEnd_AndIsDroppedOnEveryNewSession()
    {
        var browser = ReadProduct("ConditioningControlPanel", "Services", "Video", "VideoService.Browser.cs");
        var core = ReadProduct("ConditioningControlPanel", "Services", "Video", "VideoService.cs");
        Assert.Contains("_creditLinkQueuedFor = null;   // a press belongs to the clip it was made on", browser);
        var mediaEnded = browser[browser.IndexOf("private void OnBrowserMediaEnded()", StringComparison.Ordinal)..];
        mediaEnded = mediaEnded[..mediaEnded.IndexOf("\n        }", StringComparison.Ordinal)];
        Assert.Contains("_maxLenCapTimer?.Stop();", mediaEnded);
        Assert.Contains("_spawnTimes.Clear();", mediaEnded);
        Assert.Contains("CanOpenLink(IsStrictActive", mediaEnded);   // re-gated at open time
        Assert.DoesNotContain("_creditLinkQueuedFor", core);          // no other door opens it

        var html = ReadProduct("ConditioningControlPanel", "Resources", "web", "player", "index.html");
        Assert.Contains("id=\"credit\"", html);
        Assert.Contains("id=\"outro\"", html);
    }

    private static string ReadProduct(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, Path.Combine(parts));
        Assert.True(File.Exists(path), $"product source missing: {path}");
        return File.ReadAllText(path);
    }
}
