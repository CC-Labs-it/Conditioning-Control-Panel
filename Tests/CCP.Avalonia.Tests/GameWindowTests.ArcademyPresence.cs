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
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private sealed class PresenceFeed : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"v\":1,\"now\":5,\"students\":[{\"id\":\"x\"}]}"),
            });
    }

    /// <summary>WPF ArcademyHostService.cs:262,4361,6106: the open campus gets the snapshot as a presence
    /// frame, and panic or close detaches the poll in the same tick so no later frame reaches the page.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Arcademy_PresenceFrameReachesPageAndStopsOnClose(bool panic) => Run((shell, _) =>
    {
        var oldHttp = ArcademyPresenceService.Http;
        ArcademyPresenceService.Http = new HttpClient(new PresenceFeed());
        var w = new GameWindow(GameWindow.ArcademySpec);
        var frames = new List<JObject>();
        w.Posted += json => { var o = JObject.Parse(json); if ((string?)o["type"] == "presence") frames.Add(o); };
        try
        {
            w.Show();
            var end = DateTime.UtcNow.AddSeconds(5);
            while (frames.Count == 0 && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            var frame = Assert.Single(frames);
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
        finally { w.Close(); ArcademyPresenceService.Http = oldHttp; }
    });
}
