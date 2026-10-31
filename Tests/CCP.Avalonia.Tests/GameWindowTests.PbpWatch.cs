using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    [Fact]
    public void PbpWatch_FreshExistingAndBootingBoardsReceiveOneSpectateIntent() => Run((shell, _) =>
    {
        CoreSettings.Current.PbpLetPeopleWatch = true;
        string first = "m_" + new string('a', 16), second = "m_" + new string('b', 16);
        Assert.Null(GameWindow.HostSpectate("not-a-match"));
        var page = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.AvailableSubjectsTabView>("AvailableSubjectsTab")!;
        page.ReadGates = () => ConditioningControlPanel.Services.Lobby.LobbyGates.From(true, false, false);
        var row = new ConditioningControlPanel.Services.Lobby.LobbyRow
        {
            Game = ConditioningControlPanel.Services.Lobby.LobbyGame.Chess,
            State = ConditioningControlPanel.Services.Lobby.LobbyRowState.Playing,
            Key = first, CanWatch = true, Watchers = 3,
        };
        page.PaintSnapshot(new ConditioningControlPanel.Services.Lobby.LobbySnapshot(
            Array.Empty<ConditioningControlPanel.Services.Lobby.LobbyRow>(), new[] { row },
            Array.Empty<ConditioningControlPanel.Services.Lobby.LobbyRow>(), true), page.ReadGates());
        shell.Show(); // the shared game fixture starts with the panel hidden behind the launcher
        shell.ShowTab("availablesubjects");
        page.SelectColumn(1);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        shell.UpdateLayout();
        var watch = page.FindControl<global::Avalonia.Controls.ItemsControl>("LobbyPlayingList")!
            .GetVisualDescendants().OfType<global::Avalonia.Controls.Button>().Single();
        Assert.True(watch.IsEnabled);
        watch.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        w.PbpOsAnimations = () => Task.FromResult(true);
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(new[] { "pbp:settings", "pbp:identity" }, sent.Select(o => (string?)o["type"]));
        Assert.Equal(first, (string?)sent[0]["spectateMatchId"]);
        Assert.True((bool)sent[0]["letPeopleWatch"]!);
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(2, sent.Count);
        Assert.Same(w, GameWindow.HostSpectate(second));
        Assert.Equal("pbp:friend", (string?)sent.Last()["type"]);
        Assert.Equal("spectate", (string?)sent.Last()["mode"]);
        Assert.Equal(second, (string?)sent.Last()["matchId"]);
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Assert.False(GameWindow.IsAnyOpen());

        // A board already booting queues the intent until identity, with no settings target.
        var booting = GameWindow.Launch(GameWindow.PbpId)!;
        var motion = new TaskCompletionSource<bool>();
        booting.PbpOsAnimations = () => motion.Task;
        sent.Clear();
        booting.Posted += json => sent.Add(JObject.Parse(json));
        Assert.Same(booting, GameWindow.HostSpectate(first));
        Assert.Empty(sent);
        booting.HandleMessage("{\"type\":\"ready\"}");
        Assert.Same(booting, GameWindow.HostSpectate(second));
        Assert.Empty(sent);
        motion.SetResult(true);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "pbp:settings", "pbp:identity", "pbp:friend" }, sent.Select(o => (string?)o["type"]));
        Assert.Equal(JTokenType.Null, sent[0]["spectateMatchId"]!.Type);
        Assert.Equal(second, (string?)sent[2]["matchId"]);
    });

    [Fact]
    public void PbpLaunch_NotesDayLogOncePerWindow() => Run((_, _) =>
    {
        using var log = new FeatureDayLogService(() => null,
            System.IO.Path.Combine(TestUserDataProfile.Root, "pbp-day-log.json"));
        var property = typeof(ConditioningControlPanel.Avalonia.App).GetProperty("FeatureDayLog",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        property.SetValue(null, log);
        try
        {
            var key = SeasonFeatureKeys.ToDayLogEvent(SeasonFeatureKeys.PieceByPiece)!;
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            Assert.Equal(1, log.Log.Days.Single().Get(key));
            Assert.Same(w, GameWindow.Launch(GameWindow.PbpId));
            Assert.Equal(1, log.Log.Days.Single().Get(key));
            w.Close();
            GameWindow.Launch(GameWindow.PbpId);
            Assert.Equal(2, log.Log.Days.Single().Get(key));
        }
        finally { property.SetValue(null, previous); }
    });
}
