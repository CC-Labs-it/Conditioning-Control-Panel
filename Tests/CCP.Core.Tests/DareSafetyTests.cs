using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

public sealed class DareSafetyTests
{
    private sealed class Api : IDaresApi
    {
        public readonly TaskCompletionSource<JObject?> StartReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            if (op == "start") return StartReply.Task;
            var bet = DaresApiCoreTests.BetJson(role: "to", state: "settled", winner: "from", debt: "owed");
            bet["stakes"]!["to"] = new JObject { ["kind"] = "lockdown", ["amount"] = 10 };
            return Task.FromResult<JObject?>(new JObject { ["ok"] = true, ["me"] = new JObject(), ["bets"] = new JArray(bet) });
        }
    }
    private sealed class Host : IDareHost
    {
        public bool Active;
        public string? Account() => "u_me";
        public int TzMinutes() => 0;
        public bool ChasterReady() => false;
        public int BookLockSeconds(int seconds) => 0;
        public void AdoptSp(string account, int sp) { }
        public bool LockdownActive => Active;
        public bool StartLockdown(TimeSpan duration) { Active = true; return true; }
        public event Action<TimeSpan, TimeSpan>? LockdownEnded { add { } remove { } }
    }
    private sealed class Camera : IDareCameraProbe
    {
        public bool Running { get; set; }
        public bool FaceSeen { get; set; }
        public bool HeldOff { get; set; }
        public int Starts, Stops;
        public bool TryStart() { Starts++; Running = true; return true; }
        public void Stop() { Stops++; Running = false; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_stop_or_dispose_invalidates_a_delayed_lockdown_start(int mode)
    {
        var api = new Api();
        var host = new Host();
        var keeper = new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore());
        using var service = new DaresService(api, host, new DareMeter(), keeper);
        await service.RefreshAsync();
        var pending = service.ActAsync("b1", DareOp.StartLockdown);
        Assert.False(pending.IsCompleted);
        if (mode == 1) service.Dispose();
        else { service.CancelPending(); if (mode == 2) service.Resume(); }
        api.StartReply.SetResult(new JObject { ["ok"] = true, ["status"] = "serving" });
        Assert.Equal(DareActResult.Refused, await pending);
        Assert.False(host.Active);
        Assert.Null(keeper.Pending);
    }

    [Fact]
    public void Camera_check_closes_only_the_camera_it_opened()
    {
        var now = DareMeterCoreTests.T0.AddMinutes(1);
        var meter = new DareMeter();
        meter.Track(new[] { DareMeterCoreTests.Bet("camera", DareKind.Pics, "100", camera: true) });
        var camera = new Camera();
        var check = new DareCameraCheck(camera);
        Assert.True(check.Step(meter, new DareLive(true, false, false), now));
        Assert.Equal(1, camera.Starts);
        Assert.False(check.Step(meter, default, now.AddSeconds(1)));
        Assert.Equal(1, camera.Stops);
        camera.Running = true;
        Assert.True(check.Step(meter, new DareLive(true, false, false), now.AddSeconds(2)));
        Assert.False(check.Step(meter, default, now.AddSeconds(3)));
        Assert.Equal(1, camera.Stops);
    }

    [Fact]
    public void Camera_check_does_not_open_after_the_user_holds_it_off()
    {
        var now = DareMeterCoreTests.T0.AddMinutes(1);
        var meter = new DareMeter();
        meter.Track(new[] { DareMeterCoreTests.Bet("camera", DareKind.Pics, "100", camera: true) });
        var camera = new Camera { HeldOff = true };
        var check = new DareCameraCheck(camera);
        Assert.True(check.Step(meter, new DareLive(true, false, false), now));
        Assert.Equal(DareCameraState.HeldOff, check.State);
        Assert.Equal(0, camera.Starts);
    }
}
