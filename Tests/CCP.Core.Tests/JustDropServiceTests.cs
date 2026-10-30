using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Just Drop's rules as WPF has them (JustDropService.cs / JustDropHostService.cs, now Core):
/// the door is the server's flag, the app's token rides only the handoff request, and a drop pays
/// once per order by the host's own clock. No network: the config GET is a fake.</summary>
[Collection(SessionStatics.Name)]
public sealed class JustDropServiceTests
{
    private static Func<Task<(bool, string?)>> Answer(bool ok, string? body) => () => Task.FromResult<(bool, string?)>((ok, body));

    [Fact]
    public void TheDoorIsShutUntilTheServerSaysSo_AndTheFlagRaisesTheEvent()
    {
        var prevFetch = JustDropService.ConfigFetchOverride;
        int raised = 0;
        EventHandler onChange = (_, _) => raised++;
        JustDropService.AvailabilityChanged += onChange;
        try
        {
            JustDropService.SetServerEnabledForTests(false);
            foreach (var fail in new[] { Answer(false, null), Answer(true, "not json"), Answer(true, "{\"enabled\":false}"),
                                         () => throw new InvalidOperationException("offline") })
            {
                JustDropService.ConfigFetchOverride = fail;
                JustDropService.RefreshAvailabilityAsync().GetAwaiter().GetResult();
                Assert.False(JustDropService.DoorAvailable);
            }
            Assert.Equal(0, raised);

            JustDropService.ConfigFetchOverride = Answer(true, "{\"enabled\":true}");
            JustDropService.RefreshAvailabilityAsync().GetAwaiter().GetResult();
            Assert.True(JustDropService.DoorAvailable);
            Assert.Equal(1, raised);
            JustDropService.RefreshAvailabilityAsync().GetAwaiter().GetResult();   // same answer: no event
            Assert.Equal(1, raised);
        }
        finally
        {
            JustDropService.AvailabilityChanged -= onChange;
            JustDropService.ConfigFetchOverride = prevFetch;
            JustDropService.SetServerEnabledForTests(false);
        }
    }

    [Fact]
    public void SignedOut_TheShopOpensDirectly_SignedIn_ThroughTheHandoff_AndTheTokenIsNeverInTheUrl()
    {
        Assert.Equal("https://app.cclabs.app/dashboard/express", JustDropHandoff.BuildStartUrl(JustDropHandoff.ShopPath, null, "tok"));
        Assert.Equal("https://app.cclabs.app/dashboard/express", JustDropHandoff.BuildStartUrl(JustDropHandoff.ShopPath, "u1", ""));
        var url = JustDropHandoff.BuildStartUrl(JustDropHandoff.ShopPath, "u 1", "SECRET-TOKEN");
        Assert.Equal("https://app.cclabs.app/api/auth/desktop-session?unified_id=u%201&next=%2Fdashboard%2Fexpress", url);
        Assert.DoesNotContain("SECRET-TOKEN", url);
        Assert.Equal("/express/play?order=AB%2012", JustDropHandoff.ReplayPath(" AB 12 "));
    }

    [Theory]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress", true)]
    [InlineData("https://APP.cclabs.app/api/auth/desktop-session", true)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session/?next=x", true)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session-evil", false)]
    [InlineData("http://app.cclabs.app/api/auth/desktop-session", false)]
    [InlineData("https://app.cclabs.app:8443/api/auth/desktop-session", false)]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session/extra", false)]
    [InlineData("https://app.cclabs.app/dashboard/express", false)]
    [InlineData("https://cclabs.app/api/auth/desktop-session", false)]
    [InlineData("https://app.cclabs.app.evil.example/api/auth/desktop-session", false)]
    [InlineData("https://user@app.cclabs.app/api/auth/desktop-session", false)]
    public void TheTokenRidesOnlyTheHandoffRequest(string request, bool carries)
    {
        Assert.Equal(carries ? "tok" : null, JustDropHandoff.AuthHeaderFor(new Uri(request), "tok"));
        Assert.Null(JustDropHandoff.AuthHeaderFor(new Uri(request), ""));
    }

    [Fact]
    public void TheBridge_PaysAFirstPlayOnce_ByTheHostClock_AndIgnoresForeignFrames()
    {
        var file = Path.Combine(Path.GetTempPath(), "jd-credited-" + Guid.NewGuid().ToString("N") + ".json");
        var (prevFile, prevXp, prevNow, prevNotify) =
            (CreditedOrders.FilePathOverride, CoreProgression.AddXPProvider, JustDropService.UtcNow, CoreProgram.NotifyProvider);
        var s = CoreSettings.Current;
        var (prevDay, prevCount, prevLevel) = (s.JustDropXpDayKey, s.JustDropCreditedToday, s.PlayerLevel);
        var paid = new List<(double Xp, string Source)>();
        var toasts = new List<string>();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            CreditedOrders.FilePathOverride = file;
            CoreProgression.AddXPProvider = (xp, source) => paid.Add((xp, source));
            CoreProgram.NotifyProvider = (message, kind, _) => toasts.Add(kind);
            JustDropService.UtcNow = () => now;
            (s.JustDropXpDayKey, s.JustDropCreditedToday, s.PlayerLevel) = ("", 0, 1);

            static string Frame(string type, string code, string size = "L", string source = "justdrop", int v = 1) =>
                "{\"source\":\"" + source + "\",\"v\":" + v + ",\"type\":\"" + type + "\",\"payload\":{\"orderCode\":\"" + code
                + "\",\"sizeId\":\"" + size + "\",\"durationSec\":99999}}";

            JustDropService.HandleWebMessage(Frame("session-complete", "A", source: "site"));
            JustDropService.HandleWebMessage(Frame("session-complete", "A", v: 2));
            JustDropService.HandleWebMessage("not json");
            Assert.Empty(paid);
            Assert.Empty(toasts);

            // The page's durationSec is never trusted; the host's 30 minutes on an L are.
            JustDropService.HandleWebMessage(Frame("session-start", "A"));
            now = now.AddSeconds(1800);
            JustDropService.HandleWebMessage(Frame("session-complete", "A"));
            Assert.Equal(new[] { (1000.0, "Other") }, paid);
            Assert.Equal(1, s.JustDropCreditedToday);

            // The same order again is a replay: a toast, no XP.
            JustDropService.HandleWebMessage(Frame("session-start", "A"));
            now = now.AddSeconds(1800);
            JustDropService.HandleWebMessage(Frame("session-complete", "A"));
            Assert.Single(paid);
            Assert.Equal(new[] { "Success", "Success" }, toasts);

            // No start the host saw: a taste (40), whatever the page claims; the 4th drop pays a quarter.
            JustDropService.HandleWebMessage(Frame("session-complete", "B"));
            Assert.Equal((40.0, "Other"), paid[^1]);
            JustDropService.HandleWebMessage(Frame("session-start", "C", size: "S"));
            now = now.AddSeconds(300);
            JustDropService.HandleWebMessage(Frame("session-complete", "C", size: "S"));
            Assert.Equal(300.0, paid[^1].Xp);
            JustDropService.HandleWebMessage(Frame("session-start", "D", size: "S"));
            now = now.AddSeconds(300);
            JustDropService.HandleWebMessage(Frame("session-complete", "D", size: "S"));
            Assert.Equal(75.0, paid[^1].Xp);
        }
        finally
        {
            CreditedOrders.FilePathOverride = prevFile;
            CoreProgression.AddXPProvider = prevXp;
            CoreProgram.NotifyProvider = prevNotify;
            JustDropService.UtcNow = prevNow;
            (s.JustDropXpDayKey, s.JustDropCreditedToday, s.PlayerLevel) = (prevDay, prevCount, prevLevel);
            try { File.Delete(file); } catch { }
        }
    }
}
