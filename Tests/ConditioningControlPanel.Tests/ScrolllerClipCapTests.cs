using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Clips cap at 720p by the short side (Scrolller asked us to cut bandwidth, 2026-10-16).
/// Source shapes below are real ones from their API: HD posts carry 1080p + 480p only.
/// </summary>
public class ScrolllerClipCapTests
{
    private static JArray Sources(params (int w, int h, string ext)[] s)
    {
        var a = new JArray();
        foreach (var (w, h, ext) in s)
            a.Add(new JObject { ["url"] = $"https://v.scrolller.com/x-{w}x{h}.{ext}", ["width"] = w, ["height"] = h });
        return a;
    }

    private static string? PickClip(JArray s) =>
        (string?)ScrolllerSource.PickSource(s, RemoteMediaFormats.IsRemoteVideo, 720, shortSide: true)?["url"];

    [Fact]
    public void HdLandscape_TakesThe480pRendition()
    {
        var s = Sources((1920, 1080, "mp4"), (854, 480, "mp4"));
        Assert.EndsWith("854x480.mp4", PickClip(s));
    }

    [Fact]
    public void HdPortrait_IsMeasuredByItsShortSide()
    {
        var s = Sources((608, 1080, "mp4"), (270, 480, "mp4"));
        Assert.EndsWith("608x1080.mp4", PickClip(s));   // 608 wide = under 720p
    }

    [Fact]
    public void ClipUnderTheCap_KeepsItsOnlyRendition()
    {
        Assert.EndsWith("720x404.mp4", PickClip(Sources((720, 404, "mp4"))));
    }

    [Fact]
    public void OnlyAnOversizedRendition_StillPlays()
    {
        Assert.EndsWith("960x1400.mp4", PickClip(Sources((960, 1400, "mp4"))));
    }
}
