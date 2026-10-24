using System.Linq;
using System.Net.Http;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The app tells Scrolller who it is (owner, 2026-10-17): its own user-agent, never a browser's,
/// and never a scrolller.com Origin or Referer, on the API client and the media client alike.
/// </summary>
public class ScrolllerHonestIdentityTests
{
    public static TheoryData<string> Clients => new() { "api", "media" };

    private static HttpClient Make(string which)
        => which == "api" ? ScrolllerSource.CreateClient() : RemoteMediaCache.CreateClient();

    [Theory]
    [MemberData(nameof(Clients))]
    public void Sends_the_apps_own_user_agent(string which)
    {
        using var c = Make(which);
        var ua = string.Join(" ", c.DefaultRequestHeaders.GetValues("User-Agent"));
        Assert.StartsWith("ConditioningControlPanel/" + UpdateService.AppVersion, ua);
        Assert.Contains("cclabs.app", ua);
        Assert.DoesNotContain("Mozilla", ua);
        Assert.DoesNotContain("Chrome", ua);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void Never_poses_as_scrolller_com(string which)
    {
        using var c = Make(which);
        Assert.False(c.DefaultRequestHeaders.Contains("Origin"));
        Assert.Null(c.DefaultRequestHeaders.Referrer);
        Assert.DoesNotContain(c.DefaultRequestHeaders, h => h.Value.Any(v => v.Contains("scrolller.com")));
    }
}
