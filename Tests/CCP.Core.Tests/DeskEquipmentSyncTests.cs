using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;
namespace ConditioningControlPanel.Tests;

public sealed class DeskEquipmentSyncTests
{
    private const string Hat = "desk.hat.party";
    private sealed class Wire : HttpMessageHandler
    {
        public readonly List<HttpMethod> Methods = new();
        public Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => Task.FromResult(Json(404, "{}"));
        public JObject? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Methods.Add(r.Method);
            if (r.Content != null) Body = JObject.Parse(await r.Content.ReadAsStringAsync(ct));
            return await Reply(ct);
        }
    }
    private static HttpResponseMessage Json(int code = 200, string body = "{\"ok\":true,\"equip\":{\"desk\":{\"hat\":\"desk.hat.party\"},\"updatedAt\":9999999999999}}")
        => new((HttpStatusCode)code) { Content = new StringContent(body) };
    private static DeskEquipmentSync Make(Wire wire, AppSettings s, Action<Action>? post = null, Func<(string,string)?>? identity = null)
        => new(new HttpClient(wire), () => s, () => {}, identity ?? (() => ("account", "test")),
            () => new[] { "cos." + Hat }, post ?? (a => a()), "https://example.invalid");
    [Fact]
    public async Task MissingRouteKeepsPickAndNextLaunchPushesInsteadOfPulling()
    {
        var s = new AppSettings(); var wire = new Wire();
        using (var first = Make(wire, s)) await first.SaveAsync(new(Hat, "desk.toy.rubber-duck"));
        Assert.Equal(Hat, s.EmiDeskHat); Assert.Null(s.EmiDeskToy); Assert.True(s.EmiDeskEquipPushOwed);
        wire.Reply = _ => Task.FromResult(Json());
        using var next = Make(wire, s); await next.PullOnceAsync(); await next.PullOnceAsync();
        Assert.Equal(new[] { HttpMethod.Put, HttpMethod.Put }, wire.Methods);
        Assert.Equal("desk", (string?)wire.Body!["family"]); Assert.Equal("account", (string?)wire.Body["unified_id"]);
        Assert.Equal(Hat, (string?)wire.Body["value"]!["hat"]); Assert.False(s.EmiDeskEquipPushOwed);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task QueuedGetCannotOverwriteAPickOrAnotherAccount(bool switchAccount)
    {
        var s = new AppSettings(); var wire = new Wire { Reply = _ => Task.FromResult(Json()) };
        var queued = new List<Action>(); string account = "account";
        using var sync = Make(wire, s, queued.Add, () => (account,"test"));
        await sync.PullOnceAsync();
        if (switchAccount) account = "other"; else await sync.SaveAsync(new(null,null));
        foreach (var a in queued) a();
        Assert.Null(s.EmiDeskHat);
        if (!switchAccount) Assert.False(s.EmiDeskEquipPushOwed);
    }
    [Fact]
    public async Task PanicCancelsRequestAndRejectsQueuedAdoption()
    {
        var s = new AppSettings(); var wire = new Wire(); bool cancelled = false;
        wire.Reply = async ct => { try { await Task.Delay(Timeout.Infinite,ct); } catch (OperationCanceledException) { cancelled = true; throw; } return Json(); };
        using var sync = Make(wire,s);
        var pending = sync.PullOnceAsync(); sync.Stop(); await pending;
        Assert.True(cancelled); Assert.Null(s.EmiDeskHat);
        var queued = new List<Action>(); wire.Reply = _ => Task.FromResult(Json());
        using var other = Make(wire,s,queued.Add); await other.PullOnceAsync(); other.Stop();
        Assert.Single(queued); queued[0](); Assert.Null(s.EmiDeskHat);
    }
    [Fact]
    public async Task NewerServerAdoptsButMalformedAndSignedOutNeverErase()
    {
        var s = new AppSettings(); var wire = new Wire { Reply = _ => Task.FromResult(Json()) };
        using (var sync = Make(wire,s)) await sync.PullOnceAsync();
        Assert.Equal(Hat,s.EmiDeskHat); Assert.Equal(9999999999999,s.EmiDeskEquipUpdatedMs);
        wire.Reply = _ => Task.FromResult(Json(200,"junk"));
        using (var sync = Make(wire,s)) await sync.PullOnceAsync();
        using (var sync = Make(wire,s,identity: () => null)) await sync.SaveAsync(new(Hat,null));
        Assert.Equal(Hat,s.EmiDeskHat); Assert.True(s.EmiDeskEquipPushOwed); Assert.Equal(2,wire.Methods.Count);
        Assert.Null(DeskEquipment.Clamp(new("../../bad",Hat),Array.Empty<string>()).Hat);
        Assert.Null(DeskEquipment.Clamp(new(null,Hat),null).Toy);
        Assert.False(DeskEquipmentSync.ShouldAdopt(10,10));
    }
}
