using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ShellEventFxTests
{
    [Fact]
    public void HiddenAchievementAndQuestUseTheirRailAnchorsAndVisibleTabsKeepOneBurst() => WithShell((shell, engine, quests) =>
    {
        Assert.Equal(0, Bursts(shell));
        engine.CheckLevelAchievements(10);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Bursts(shell));
        AssertAt(shell, shell.NavAnchorForTab("achievements")!);

        var pop = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
        quests.TrackBubblesPopped(pop.TargetValue);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Bursts(shell));
        Assert.Equal(1, QuestBursts(shell));
        var questCanvas = Layer(shell, "_questCompletionBurst");
        Assert.True(questCanvas.IsRunning);
        shell.StopEventFx();
        Assert.False(questCanvas.IsRunning);
        Assert.Null(questCanvas.Parent);

        shell.ShowTab("achievements");
        Dispatcher.UIThread.RunJobs();
        var achievements = shell.Named<AchievementsTabView>("AchievementsTab")!;
        engine.CheckLevelAchievements(20);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Bursts(shell));
        Assert.Equal(1, achievements.Bursts); // the tile owns the moment; no second rail burst

        shell.ShowTab("quests");
        Dispatcher.UIThread.RunJobs();
        quests.TrackSpiralMinutes(10_000);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Bursts(shell));
        Assert.Equal(1, shell.Named<QuestsTabView>("QuestsTab")!.Bursts);
    });

    [Fact]
    public void LevelBurstUsesTrackCapAndMotionMinimizeCloseAndPanicStopIt() => WithShell((shell, engine, quests) =>
    {
        var s = CoreSettings.Current;
        s.OfflineMode = true;
        s.OfflineUsername = "event-fx-test";
        s.PlayerLevel = 1;
        s.PlayerXP = 0;
        void Level() { ProgressionBank.Add(XpCurve.GetXPForLevel(s.PlayerLevel, XpCurve.EpochOf(s)), "Session"); Dispatcher.UIThread.RunJobs(); }

        Level();
        Assert.Equal(1, Bursts(shell));
        AssertAt(shell, shell.Named<Border>("XPBarTrack")!, rightEdge: true);
        var canvas = Layer(shell);
        Assert.True(canvas.IsRunning);

        var stop = Assert.Single(PanicSurfaces.All, x => x.Id == "event-fx");
        stop.Stop(shell);
        Assert.False(canvas.IsRunning);
        Assert.Null(canvas.Parent);
        stop.Stop(shell); // idle panic is harmless

        s.MotionLevel = MotionLevel.Off;
        Level();
        Assert.Equal(1, Bursts(shell));
        s.MotionLevel = MotionLevel.Reduced;
        Level();
        Assert.Equal(1, Bursts(shell));
        s.MotionLevel = MotionLevel.Full;
        shell.WindowState = WindowState.Minimized;
        Level();
        Assert.Equal(1, Bursts(shell));
        shell.WindowState = WindowState.Normal;
        Level();
        Assert.Equal(2, Bursts(shell));
        canvas = Layer(shell);
        shell.Close();
        Assert.False(canvas.IsRunning);
        Assert.Null(canvas.Parent);
        Level();
        engine.CheckLevelAchievements(75);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, Bursts(shell)); // service subscriptions cannot pin or repaint a closed shell
    });

    // Reading the existing EventBurstLayer counter lets the same behavior test fail on the old
    // shell, which has no burst layer at all, before the implementation is applied.
    internal static int Bursts(MainShellWindow shell) =>
        typeof(MainShellWindow).GetField("_eventBursts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(shell)
            is EventBurstLayer layer ? layer.Count : 0;

    private static int QuestBursts(MainShellWindow shell) => ((EventBurstLayer)typeof(MainShellWindow)
        .GetField("_questCompletionBurst", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!).Count;

    private static AmbientFxCanvas Layer(MainShellWindow shell, string field = "_eventBursts")
    {
        var bursts = typeof(MainShellWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        return Assert.IsType<AmbientFxCanvas>(typeof(EventBurstLayer)
            .GetField("_layer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bursts));
    }

    private static void AssertAt(MainShellWindow shell, Control anchor, bool rightEdge = false)
    {
        var layer = Layer(shell);
        var host = OverlayLayer.GetOverlayLayer(anchor)!;
        var at = anchor.TranslatePoint(new Point(rightEdge ? anchor.Bounds.Width : anchor.Bounds.Width / 2,
            anchor.Bounds.Height / 2), host)!.Value;
        Assert.Equal(at.X - 280, Canvas.GetLeft(layer), 3);
        Assert.Equal(at.Y - 280, Canvas.GetTop(layer), 3);
    }

    private static void WithShell(Action<MainShellWindow, AchievementEngine, QuestService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-shell-event-fx-").FullName;
        var (oldSettings, oldAchievements, oldQuests) = (CoreSettings.ServiceProvider, AvApp.Achievements, AvApp.Quests);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.AvatarEnabled = false;
        s.SuppressPerkNotifications = true;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        MainShellWindow? shell = null;
        using var quests = new QuestService(null, dir);
        try
        {
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            AvApp.Quests = quests;
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            SetAchievements(engine);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(shell, engine, quests);
        }
        finally
        {
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            quests.Dispose();
            CoreSettings.ServiceProvider = oldSettings;
            AvApp.Quests = oldQuests;
            SetAchievements(oldAchievements);
            Directory.Delete(dir, true);
        }
    });

    private static void SetAchievements(AchievementEngine? engine) => typeof(AvApp)
        .GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, engine);
}
