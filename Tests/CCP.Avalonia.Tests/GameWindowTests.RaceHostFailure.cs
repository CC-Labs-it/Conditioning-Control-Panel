using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Classic_FailureClosesAndOpenRequestsAudioAndNotesUsage() => RunRace("rt.original.03", (_, _, _) =>
    {
        var packs = new List<string>();
        var usage = new List<string>();
        var oldPack = GameWindow.RaceRequestPack;
        var oldUsage = GameWindow.RaceNoteUsage;
        try
        {
            GameWindow.RaceRequestPack = packs.Add;
            GameWindow.RaceNoteUsage = usage.Add;
            var w = OpenRetiredRace();
            Assert.Equal(new[] { "audio-web" }, packs);
            Assert.Equal(new[] { "race" }, usage);
            Assert.False((bool)JObject.FromObject(w.RaceInitMessage())["settings"]!["cloud"]!);
            GameWindow.RaceCloudOffered = true;
            Assert.True((bool)JObject.FromObject(w.RaceInitMessage())["settings"]!["cloud"]!);
            Assert.True(w.RaceWatchRunning);
            w.Web.OnBrowserProcessFailed();
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.False(w.RaceWatchRunning);
        }
        finally { GameWindow.RaceCloudOffered = false; GameWindow.RaceRequestPack = oldPack; GameWindow.RaceNoteUsage = oldUsage; }
    });

    [Fact]
    public void Glassroad_UnresponsiveWaitsAndFailureCloses() => RunGlassroad((_, _, _) =>
    {
        var w = GameWindow.Launch(GameWindow.GlassroadId)!;
        Assert.True(w.GlassroadWatchRunning);
        w.Web.OnBrowserProcessUnresponsive();
        Assert.Equal("idle", w.CheckGlassroadWatch(DateTime.UtcNow.AddSeconds(60)));
        Assert.True(GameWindow.IsAnyOpen());
        w.Web.OnBrowserProcessFailed();
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(w.GlassroadWatchRunning);
    });

    [Fact]
    public void Glassroad_NetReplyCallsMergedRecovery() => RunGlassroad((_, _, fake) =>
    {
        var old = GameWindow.GlassroadMergedRecovery;
        var seen = new List<(int, string?)>();
        try
        {
            GameWindow.GlassroadMergedRecovery = (status, body) => { seen.Add((status, body)); return true; };
            fake.Status = System.Net.HttpStatusCode.Conflict;
            fake.Reply = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_123\"}";
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            w.Web.OnNavigationCompleted(w.PageUrl);
            w.Web.OnWebMessage("{\"type\":\"net\",\"id\":\"a\",\"method\":\"GET\",\"path\":\"/v2/race/access\"}");
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal((409, fake.Reply), Assert.Single(seen));
            w.Close();
        }
        finally { GameWindow.GlassroadMergedRecovery = old; }
    });
}
