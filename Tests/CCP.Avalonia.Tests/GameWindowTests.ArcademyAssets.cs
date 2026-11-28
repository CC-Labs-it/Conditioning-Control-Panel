using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Arcademy_AssetRequestsEndEmptyWithTheirOwnIds() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("arcademy", "launcher_game_arcademy_title", "arcademy/index.html"));
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        try
        {
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"one\"}");
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"two\",\"subs\":[\"cats\"],\"tag\":\"target\"}");
            Assert.Equal(2, sent.Count);
            Assert.Equal("one", (string?)sent[0]["reqId"]);
            Assert.Equal("two", (string?)sent[1]["reqId"]);
            foreach (var frame in sent)
            {
                Assert.Equal("assets", (string?)frame["type"]);
                Assert.True((bool?)frame["done"]);
                Assert.Empty((JArray)frame["urls"]!);
            }
        }
        finally { w.Close(); }
    });
}
