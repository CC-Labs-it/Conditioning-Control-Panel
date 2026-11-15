using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Arcademy_AnnexStats_RoundTripAndOffline() => Run((_, _) =>
    {
        var oldFetch = GameWindow.FetchAnnexStats;
        var oldOffline = CoreSettings.Current.OfflineMode;
        var calls = 0;
        GameWindow.FetchAnnexStats = () => { calls++; return Task.FromResult("{\"students\":42}"); };
        var w = new GameWindow(new GameWindow.Game("arcademy", "launcher_game_arcademy_title", "arcademy/index.html"));
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        try
        {
            CoreSettings.Current.OfflineMode = false;
            w.HandleMessage("{\"type\":\"annex-stats\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(42, (int?)Assert.Single(sent)["body"]?["students"]);
            sent.Clear();
            CoreSettings.Current.OfflineMode = true;
            w.HandleMessage("{\"type\":\"annex-stats\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(JTokenType.Null, Assert.Single(sent)["body"]!.Type);
            Assert.Equal(1, calls);
        }
        finally { w.Close(); GameWindow.FetchAnnexStats = oldFetch; CoreSettings.Current.OfflineMode = oldOffline; }
    });
}
