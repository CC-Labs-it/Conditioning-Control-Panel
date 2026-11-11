using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeashEventCuesTests
{
    private sealed class Api : ILeashApi
    {
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default) => Task.FromResult<JObject?>(null);
    }
    [Fact]
    public void FollowsServiceChangesAndUnsubscribes()
    {
        var first = new LeashService(new Api(), () => "me");
        var second = new LeashService(new Api(), () => "me");
        var played = new List<LeashCue>();
        void Send(LeashService svc, string id) => svc.ApplyBlock(new JObject
        {
            ["holding"] = new JArray(), ["offers"] = new JArray(), ["events"] = new JArray(new JObject
            { ["id"] = id, ["kind"] = "tug", ["from"] = new JObject { ["id"] = "friend", ["name"] = "Mia" }, ["at"] = DateTimeOffset.UtcNow.ToString("o") })
        });
        try
        {
            LeashHead.Service = first;
            using (var cues = new LeashEventCues(played.Add))
            {
                Send(first, "one"); LeashHead.Service = second;
                Send(first, "two"); Send(second, "three");
                Assert.Equal(new[] { LeashCue.Jingle, LeashCue.Jingle }, played);
            }
            Send(second, "four"); Assert.Equal  (2, played.Count);
        }
        finally { LeashHead.Service = null; }
    }
    [Theory]
    [InlineData(LeashEventKind.Tug, null, LeashCue.Jingle)]
    [InlineData(LeashEventKind.Answered, false, LeashCue.Refused)]
    [InlineData(LeashEventKind.PunishSkipped, null, LeashCue.Refused)]
    [InlineData(LeashEventKind.Reward, null, LeashCue.Gift)]
    [InlineData(LeashEventKind.Punish, null, LeashCue.Scold)]
    [InlineData(LeashEventKind.AssignMissed, null, LeashCue.Scold)]
    [InlineData(LeashEventKind.Assign, null, LeashCue.Assigned)]
    [InlineData(LeashEventKind.AssignDone, null, LeashCue.Done)]
    [InlineData(LeashEventKind.PunishDone, null, LeashCue.Done)]
    public void EventsPlayWpfCue(LeashEventKind kind, bool? accepted, LeashCue expected)
    {
        var played = new List<LeashCue>();
        using var cues = new LeashEventCues(played.Add);
        cues.OnEvent(new LeashEvent("id", kind, new LeashPerson("id", "name", null), DateTimeOffset.UtcNow, Accepted: accepted));
        Assert.Equal(expected, Assert.Single(played));
    }
    [Theory]
    [InlineData(LeashEventKind.Ended, null)]
    [InlineData(LeashEventKind.Answered, true)]
    public void OtherSurfacesOwnTheseCues(LeashEventKind kind, bool? accepted)
    {
        var played = new List<LeashCue>();
        using var cues = new LeashEventCues(played.Add);
        cues.OnEvent(new LeashEvent("id", kind, new LeashPerson("id", "name", null), DateTimeOffset.UtcNow, Accepted: accepted));
        Assert.Empty(played);
    }
}
