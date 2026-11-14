using System;
using System.Collections.Generic;
using System.Reflection;
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
    private static readonly FieldInfo OfferAccount = typeof(LeashHead).GetField("_offerAccount", BindingFlags.NonPublic | BindingFlags.Static)!;

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
        var priorService = LeashHead.Service;
        var priorSent = LeashHead.SentOffers;
        var priorPending = LeashHead.PendingOffers;
        var priorAccount = OfferAccount.GetValue(null);
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
        finally
        {
            LeashHead.Service = priorService;
            LeashHead.SentOffers = priorSent;
            LeashHead.PendingOffers = priorPending;
            OfferAccount.SetValue(null, priorAccount);
        }
    }

    [Fact]
    public void ServiceChangeTestRestoresServiceAndOfferState()
    {
        var originalService = LeashHead.Service;
        var originalSent = LeashHead.SentOffers;
        var originalPending = LeashHead.PendingOffers;
        var originalAccount = OfferAccount.GetValue(null);
        try
        {
            var service = new LeashService(new Api(), () => "prior");
            LeashHead.Service = service;
            var sent = new HashSet<string> { "sent" };
            var pending = new HashSet<string> { "pending" };
            LeashHead.SentOffers = sent;
            LeashHead.PendingOffers = pending;
            OfferAccount.SetValue(null, "prior-account");
            FollowsServiceChangesAndUnsubscribes();
            Assert.Same(service, LeashHead.Service);
            Assert.Same(sent, LeashHead.SentOffers);
            Assert.Same(pending, LeashHead.PendingOffers);
            Assert.Equal("prior-account", OfferAccount.GetValue(null));
        }
        finally
        {
            LeashHead.Service = originalService;
            LeashHead.SentOffers = originalSent;
            LeashHead.PendingOffers = originalPending;
            OfferAccount.SetValue(null, originalAccount);
        }
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
