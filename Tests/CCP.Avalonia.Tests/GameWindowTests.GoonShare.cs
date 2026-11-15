using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonInbox_ChunkCommitAbortAndOversize() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var replies = new List<JObject>();
        w.Posted += json => { var o = JObject.Parse(json); if ((string?)o["type"] == "goon-recv-result") replies.Add(o); };
        var data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a3sAAAAASUVORK5CYII=");
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var id = Guid.NewGuid().ToString("N");
        void Send(object frame) => w.HandleMessage(Newtonsoft.Json.JsonConvert.SerializeObject(frame));
        Send(new { type = "goon-recv-begin", id, sha256 = sha, mime = "image/png", bytes = data.Length });
        Send(new { type = "goon-recv-chunk", id, seq = 0, b64 = Convert.ToBase64String(data[..20]) });
        Send(new { type = "goon-recv-chunk", id, seq = 1, b64 = Convert.ToBase64String(data[20..]) });
        Assert.All(replies, x => Assert.True((bool)x["ok"]!));
        Send(new { type = "goon-recv-commit", id });
        for (var i = 0; i < 500 && replies.Count < 4; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(4, replies.Count);
        Assert.True((bool)replies.Last()["ok"]!);
        Assert.Equal(data.Length, (long)replies.Last()["bytes"]!);
        Send(new { type = "goon-recv-commit", id });
        for (var i = 0; i < 500 && replies.Count < 5; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal("unknown-job", (string?)replies.Last()["error"]);
        var other = Guid.NewGuid().ToString("N");
        Send(new { type = "goon-recv-begin", id = other, sha256 = sha, mime = "image/png", bytes = 64L * 1024 * 1024 + 1 });
        Assert.Equal("too-big", (string?)replies.Last()["error"]);
        Send(new { type = "goon-recv-begin", id = other, sha256 = sha, mime = "image/png", bytes = data.Length });
        Send(new { type = "goon-recv-abort", id = other });
        Send(new { type = "goon-recv-chunk", id = other, seq = 0, b64 = "AA==" });
        Assert.Equal("unknown-job", (string?)replies.Last()["error"]);
        Send(new { type = "goon-recv-drop", sha256 = sha });
        Assert.True((bool)replies.Last()["ok"]!);
    });
}
