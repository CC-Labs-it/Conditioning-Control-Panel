using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Funnel;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The desktop funnel beacon (contract C8): allowlisted events only, surface panel, fields cut to
/// the server's charset, no user data on the line, quiet without a proxy, and never a throw.
/// </summary>
[Collection("FunnelBeacon")]
public class FunnelBeaconTests
{
    [Theory]
    [InlineData("gate_shown", "lockdown", "basic", "gate_shown|panel|lockdown|basic")]
    [InlineData("cta_clicked", "dtrh", "patreon", "cta_clicked|panel|dtrh|patreon")]
    [InlineData("checkout_started", "prime", "web", "checkout_started|panel|prime|web")]
    [InlineData("ref", "x", "", "ref|panel|x")]
    [InlineData("gate_shown", "", "", "gate_shown|panel")]
    [InlineData("gate_shown", "", "prime", "gate_shown|panel||prime")]
    [InlineData("cta_clicked", "Remote-Control", "WEB", "cta_clicked|panel|remote_control|web")]
    [InlineData("cta_clicked", "has space", "patreon", "cta_clicked|panel||patreon")]
    [InlineData("cta_clicked", "a|b", "patreon", "cta_clicked|panel||patreon")]
    public void TheWireLine(string evt, string a, string b, string expected)
        => Assert.Equal(expected, FunnelBeacon.Body(evt, a, b));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("purchase")]
    [InlineData("Gate_Shown")]
    public void UnknownEventsNeverLeave(string? evt) => Assert.Null(FunnelBeacon.Body(evt, "a", "b"));

    [Fact]
    public void AFieldIsCappedAt32()
    {
        Assert.Equal(new string('a', 32), FunnelBeacon.Clean(new string('a', 32)));
        Assert.Equal("", FunnelBeacon.Clean(new string('a', 33)));
    }

    [Fact]
    public void TierNames()
    {
        Assert.Equal("basic", FunnelBeacon.TierName(0));
        Assert.Equal("basic", FunnelBeacon.TierName(1));
        Assert.Equal("prime", FunnelBeacon.TierName(2));
    }

    [Fact]
    public async Task PostSendsOneLineToTheFunnelRoute()
    {
        var got = new TaskCompletionSource<(string Url, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
        FunnelBeacon.BaseUrlOverride = "https://proxy.test/";
        FunnelBeacon.SendOverride = (url, body) => { got.TrySetResult((url, body)); return Task.CompletedTask; };
        try
        {
            FunnelBeacon.Post("gate_shown", "blinktrainer", "basic");
            var done = await Task.WhenAny(got.Task, Task.Delay(5000));
            Assert.Same(got.Task, done);
            Assert.Equal("https://proxy.test/v2/public/funnel", got.Task.Result.Url);
            Assert.Equal("gate_shown|panel|blinktrainer|basic", got.Task.Result.Body);
        }
        finally { FunnelBeacon.BaseUrlOverride = null; FunnelBeacon.SendOverride = null; }
    }

    [Fact]
    public async Task QuietWithoutAProxyAndNeverThrows()
    {
        var sent = 0;
        FunnelBeacon.SendOverride = (_, _) => { Interlocked.Increment(ref sent); throw new InvalidOperationException("boom"); };
        try
        {
            FunnelBeacon.BaseUrlOverride = "";
            FunnelBeacon.Post("gate_shown", "a", "basic");
            FunnelBeacon.BaseUrlOverride = "https://proxy.test";
            FunnelBeacon.Post("not_an_event");
            FunnelBeacon.Post("cta_clicked", "a", "web");   // the sender throws: swallowed
            await Task.Delay(300);
            Assert.Equal(1, Volatile.Read(ref sent));
        }
        finally { FunnelBeacon.BaseUrlOverride = null; FunnelBeacon.SendOverride = null; }
    }

}
