using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using CCP.Tests.Shared;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private sealed class PresenceFeed : HttpMessageHandler
    {
        internal readonly ManualResetEventSlim Served = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Served.Set();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"v\":1,\"now\":5,\"students\":[{\"id\":\"x\"}]}"),
            });
        }
    }

    /// <summary>WPF ArcademyHostService.cs:262,4361,6106: the open campus gets the snapshot as a presence
    /// frame, and panic or close detaches the poll in the same tick so no later frame reaches the page.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Arcademy_PresenceFrameReachesPageAndStopsOnClose(bool panic) => PresenceRoundTrip(panic);

    /// <summary>The round trip leaves the service's self id, rung and settings subscription as it found them.</summary>
    [Fact]
    public void Arcademy_PresenceTestRestoresServiceStatics()
    {
        var real = ArcademyPresenceStatics.Save();
        var known = new object?[] { "before", "off", null };   // not what the round trip leaves behind
        ArcademyPresenceStatics.Restore(known);
        try
        {
            PresenceRoundTrip(panic: false);
            Assert.Equal(known, ArcademyPresenceStatics.Save());
        }
        finally { ArcademyPresenceStatics.Restore(real); }
    }

    private static void PresenceRoundTrip(bool panic) => Run((shell, _) =>
    {
        var oldHttp = ArcademyPresenceService.Http;
        var saved = ArcademyPresenceStatics.Save();
        var feed = new PresenceFeed();
        ArcademyPresenceService.Http = new HttpClient(feed);
        var w = new GameWindow(GameWindow.ArcademySpec);
        var frames = new List<JObject>();
        var order = new List<string?>();
        w.Posted += json => { var o = JObject.Parse(json); order.Add((string?)o["type"]); if ((string?)o["type"] == "presence") frames.Add(o); };
        try
        {
            w.Show();
            // The first snapshot lands before the page listens: held, not dropped (WPF ChaosWebViewHost.Post).
            Assert.True(feed.Served.Wait(TimeSpan.FromSeconds(5)));
            for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            Assert.Empty(frames);
            w.HandleMessage("{\"type\":\"ready\"}");
            var frame = Assert.Single(frames);
            Assert.InRange(order.IndexOf("init"), 0, order.IndexOf("presence") - 1);
            Assert.Equal("x", (string?)frame["snapshot"]?["students"]?[0]?["id"]);
            Assert.True(ArcademyPresenceService.Attached);

            if (panic) PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            else w.Close();
            Assert.False(ArcademyPresenceService.Attached);
            Assert.Null(GameWindow.OpenForTests("arcademy"));
            Thread.Sleep(100);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(frames);
        }
        finally
        {
            w.Close();
            ArcademyPresenceStatics.WaitIdle();
            ArcademyPresenceStatics.Restore(saved);
            ArcademyPresenceService.Http = oldHttp;
        }
    });
}
