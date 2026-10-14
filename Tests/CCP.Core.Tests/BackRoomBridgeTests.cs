using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The host half of Back Room protocol 1 (CONTRACT section 2), requests: one reply per reqId, the
/// relay whitelist and the refusal shapes. Everything runs on a manual clock and a fake relay, so
/// nothing here waits on real time or a network. The rig (Tests/Shared/BackRoomBridgeRig.cs) is shared
/// with the lifecycle suite and the WPF head's Back Room suites.
/// </summary>
public partial class BackRoomBridgeTests
{
    private static string StationRequest(string reqId = Req, string op = "tape", string station = "slot") =>
        JsonConvert.SerializeObject(new { type = "station-request", reqId, station, op, idem = "idem-0123456789abcdef", body = new { count = 10 } });

    [Fact]
    public void Ready_PostsInitExactlyOnce()
    {
        var rig = new Rig();
        rig.Bridge.OnReady();
        rig.Bridge.OnReady();
        Assert.Single(rig.Of("init"));
    }

    [Fact]
    public async Task StationRequest_RelaysAndRepliesOnce_EvenWhenTheReqIdIsRepeated()
    {
        var rig = new Rig();
        rig.Send(StationRequest());
        rig.Send(StationRequest());   // same reqId: never a second relay, never a second reply
        rig.Relay.Next.SetResult(new BackRoomStationResult(true, 200, null, JObject.Parse("{\"ok\":true,\"sp\":9}")));

        // The single relay answered and Reply is its last step, so nothing more can arrive after this.
        await rig.SettleAsync("station-result", 1);
        var r = Assert.Single(rig.Of("station-result"));
        Assert.Single(rig.Relay.Calls);
        Assert.Equal(Req, (string?)r["reqId"]);
        Assert.True((bool)r["ok"]!);
        Assert.Equal(200, (int)r["status"]!);
        Assert.Equal(9, (int)r["body"]!["sp"]!);
    }

    [Theory]
    [InlineData("slot", "withdraw")]
    [InlineData("wheel", "tape")]
    [InlineData("SLOT", "state")]
    public async Task UnlistedOps_AreRefusedBadOp_WithoutARelay(string station, string op)
    {
        var rig = new Rig();
        rig.Send(StationRequest(op: op, station: station));
        var r = Assert.Single(await rig.SettleAsync("station-result", 1));
        Assert.Equal((false, 0, "bad_op"), ((bool)r["ok"]!, (int)r["status"]!, (string?)r["reason"]));
        Assert.Empty(rig.Relay.Calls);
    }

    [Fact]
    public async Task ShortReqId_IsRefusedBadOp()
    {
        var rig = new Rig();
        rig.Send(StationRequest(reqId: "short"));
        var r = Assert.Single(await rig.SettleAsync("station-result", 1));
        Assert.Equal("bad_op", (string?)r["reason"]);
        Assert.Equal("short", (string?)r["reqId"]);
    }

    [Fact]
    public async Task RequestAfterExit_IsRefusedClosed()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"exit\",\"reason\":\"back\"}");
        rig.Send(StationRequest());
        var r = Assert.Single(await rig.SettleAsync("station-result", 1));
        Assert.Equal((false, 0, "closed"), ((bool)r["ok"]!, (int)r["status"]!, (string?)r["reason"]));
        Assert.Empty(rig.Relay.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Close_CancelsEveryFxPrimitive_BeforeThePageSettles(bool hostClose)
    {
        // CONTRACT section 4: close (panic, app-exit) or a page exit stops overlays at once, not at
        // exit-done or the 800 ms watchdog.
        var rig = new Rig();
        if (hostClose) rig.Bridge.RequestClose("panic");
        else rig.Send("{\"type\":\"exit\",\"reason\":\"back\"}");
        Assert.Equal(1, rig.Fx.Cancelled);
        Assert.Equal(0, rig.Closed);
    }

    [Fact]
    public async Task HungRelay_GetsATimeoutFromTheGuard_AndTheLateAnswerIsDropped()
    {
        var rig = new Rig();
        // Inline continuations, no captured context, SetResult off the test thread: awaiting it waits for the late answer.
        rig.Relay.Next = new TaskCompletionSource<BackRoomStationResult>();
        await Task.Run(() => rig.Send(StationRequest()));
        rig.Clock.Fire(BackRoomBridge.ReplyGuard);
        await Task.Run(() => rig.Relay.Next.SetResult(new BackRoomStationResult(true, 200, null, null)));
        Assert.True(rig.Clock.Timers.Single().Cancelled, "the late answer ran (it cancels the guard)");
        var r = Assert.Single(rig.Of("station-result"));
        Assert.Equal("timeout", (string?)r["reason"]);
        Assert.True(BackRoomBridge.ReplyGuard < TimeSpan.FromSeconds(6), "the page gives up at 6 s");
    }
}
