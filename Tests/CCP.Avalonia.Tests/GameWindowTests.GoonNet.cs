using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData("{\"type\":\"cache-req\",\"op\":\"list\"}", "cache-list", 0)]
    [InlineData("{\"type\":\"cache-put\",\"jobId\":\"unknown\",\"seq\":0,\"b64\":\"YQ==\"}", "cache-put-result", 0)]
    [InlineData("{\"type\":\"encode-done\",\"jobId\":\"unknown\",\"ok\":true}", "cache-put-result", -1)]
    public void GoonCache_PageFrameGetsReply(string request, string type, int seq) => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.HandleMessage(request);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(frames, f => (string?)f["type"] == type && (int?)f["seq"] == seq &&
            (type == "cache-list" ? (bool?)f["last"] == true : (string?)f["error"] == "unknown-job"));
        w.Close();
    });

    [Fact]
    public void GoonCache_CloseDetachesBridge() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var host = typeof(GoonCacheBridge).GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.NotNull(host.GetValue(null));
        w.Close();
        Assert.Null(host.GetValue(null));
    });
}
