using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;
using Head = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void Race_FullscreenEchoAndClosedQueue(WindowState windowedState) => RunRace("rt.original.03", (_, _, _) =>
    {
        var w = GameWindow.Launch("race")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        sent.Clear();
        w.WindowState = windowedState;
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(windowedState, w.WindowState);
        sent.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowState.FullScreen, w.WindowState);
        Assert.True((bool)Assert.Single(sent)["on"]!);
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowState.FullScreen, w.WindowState);
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(windowedState, w.WindowState);
        Assert.False((bool)sent.Last()["on"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent);
    });

    [Fact]
    public void Race_SpiralsRepostUntilClose() => RunRace("rt.original.03", (_, _, _) =>
    {
        var w = GameWindow.Launch("race")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Single(sent.Where(o => (string?)o["type"] == "loom-list"));
        w.HandleMessage("{\"type\":\"ready\"}");
        sent.Clear();
        var gif = Convert.ToBase64String(new byte[] { 71, 73, 70, 56, 57, 97, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0x3b });
        try
        {
            Assert.True(DtrhLoomStore.Save("race-fin", gif, JObject.Parse("{\"speed\":2}"), true).Ok);
            Dispatcher.UIThread.RunJobs();
            var spiral = Assert.Single(Assert.Single(sent)["spirals"]!.Where(s => (string?)s["slug"] == "race-fin"));
            Assert.Equal("/__spirals__/loom_race-fin.gif", (string?)spiral["url"]);
            Assert.Equal(2, (int)spiral["params"]!["speed"]!);
            Assert.Equal(DtrhLoomStore.GifPathFor("race-fin"), GameWindow.Server()!.ResolveFile((string)spiral["url"]!));
            w.Close();
            sent.Clear();
            Assert.True(DtrhLoomStore.Delete("race-fin").Ok);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(sent);
        }
        finally { DtrhLoomStore.Delete("race-fin"); }
    });

    [Fact]
    public void Race_FavoritesUseTheSharedRanking() => RunRace("rt.original.03", (_, _, _) =>
    {
        var field = typeof(DtrhAssetStatsStore).GetField("_stats", BindingFlags.NonPublic | BindingFlags.Static)!;
        DtrhAssetStatsStore.TopAssets(0);
        var stats = (System.Collections.IDictionary)field.GetValue(null)!;
        var copy = Activator.CreateInstance(stats.GetType(), stats, StringComparer.OrdinalIgnoreCase);
        try
        {
            DtrhAssetStatsStore.Merge(JObject.Parse("{\"stats\":[{\"name\":\"race-favorite.gif\",\"grabs\":100}]}"));
            var w = GameWindow.Launch("race")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.Contains("race-favorite.gif", sent.Single(o => (string?)o["type"] == "favorites")["names"]!.Values<string>());
        }
        finally { field.SetValue(null, copy); }
    });

    [Theory]
    [InlineData(null)]
    [InlineData("construct")]
    [InlineData("show")]
    public void Race_BubbleBatchCreditsOnceEvenWhenXpThrows(string? popupFailure) => RunRace("rt.original.03", (_, _, _) =>
    {
        var property = typeof(Head).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var before = property.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("race-credit-").FullName;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        property.SetValue(null, engine);
        var oldQuests = Head.Quests;
        using var quests = new QuestService(null, dir);
        quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d") };
        Head.Quests = quests;
        var batches = new List<int>();
        var oldVerifier = CoreQuests.TrackProgramVerifierProvider;
        CoreQuests.TrackProgramVerifierProvider = (category, count) =>
        {
            if (category == QuestCategory.Bubbles) batches.Add(count);
        };
        try
        {
            var w = GameWindow.Launch("race")!;
            int attempts = 0;
            if (popupFailure == "construct")
                w.RaceCreateMilestonePopup = _ => { attempts++; throw new InvalidOperationException("constructor"); };
            if (popupFailure == "show")
                w.RaceShowMilestonePopup = _ => { attempts++; throw new InvalidOperationException("display"); };
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            engine.Progress.TotalBubblesPopped = 95;
            int points = CoreSettings.Current.SkillPoints;
            CoreProgression.AddXPProvider = (_, _) => throw new InvalidOperationException("test");
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-ended\",\"popped\":255}");
            Assert.Equal(255, Assert.Single(batches));
            Assert.Equal(255, quests.Progress.DailyQuests[0].CurrentProgress);
            Assert.Equal(350, engine.Progress.TotalBubblesPopped);
            Assert.Equal(points + 3, CoreSettings.Current.SkillPoints);
            var popup = w.RaceMilestonePopup;
            if (popupFailure == null)
            {
                Assert.NotNull(popup);
                Assert.True(popup.IsVisible);
                Assert.Equal(Loc.GetF("achievement_bubble_milestone_name", 300),
                    popup.FindControl<TextBlock>("TxtName")!.Text);
            }
            else Assert.Equal(1, attempts);
            w.HandleMessage("{\"type\":\"run-ended\",\"popped\":255}");
            Assert.Equal(255, Assert.Single(batches));
            Assert.Equal(255, quests.Progress.DailyQuests[0].CurrentProgress);
            Assert.Equal(350, engine.Progress.TotalBubblesPopped);
            Assert.Equal(points + 3, CoreSettings.Current.SkillPoints);
            Assert.Single(sent.Where(o => (string?)o["type"] == "payout-result"));
            w.Close();
            if (popup != null) Assert.False(popup.IsVisible);
            if (popupFailure != null) Assert.Equal(1, attempts);
        }
        finally
        {
            CoreQuests.TrackProgramVerifierProvider = oldVerifier;
            Head.Quests = oldQuests;
            quests.Dispose();
            property.SetValue(null, before);
            Directory.Delete(dir, true);
        }
    });
}
