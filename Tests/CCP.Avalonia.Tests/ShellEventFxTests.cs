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
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Helpers;
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
    public void LockedFilterUnlockFallsBackToNavAndVisibleTileStillBurstsOnce() => WithShell((shell, engine, _) =>
    {
        shell.ShowTab("achievements");
        Dispatcher.UIThread.RunJobs();
        var tab = shell.Named<AchievementsTabView>("AchievementsTab")!;
        tab.SelectFilter(AchievementsTabView.FilterLocked);
        var card = tab.Cards.Single(c => (string)c.Tag! == "plastic_initiation");
        Assert.True(card.IsVisible);
        engine.CheckLevelAchievements(10);
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsVisible);
        Assert.Equal(0, tab.Bursts);
        Assert.Equal(1, Bursts(shell));
        AssertAt(shell, shell.NavAnchorForTab("achievements")!);

        tab.SelectFilter(AchievementsTabView.FilterAll);
        shell.UpdateLayout();
        engine.CheckLevelAchievements(20);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, tab.Bursts);
        Assert.Equal(1, Bursts(shell));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Bursts(shell));
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

    [Fact]
    public void EnhancementClickConfirmsBuysAndCelebratesBeforeRebuild() => WithShell((shell, engine, _) =>
    {
        var clock = new FxClock();
        BeatLoop.Time = clock;
        CoreSettings.Current.SkillPoints = 100;
        shell.ShowTab("enhancements");
        Dispatcher.UIThread.RunJobs();
        var tab = shell.Named<EnhancementsTabView>("EnhancementsTab")!;
        var skill = SkillDefinition.All.First(x => SkillTreeRules.CanPurchaseSkill(CoreSettings.Current, x.Id) && !x.IsSecret);
        var card = tab.GetVisualDescendants().OfType<Panel>().First(x => (string?)x.Tag == skill.Id);
        int confirmed = 0, bought = 0;
        tab.ConfirmPurchase = (_, title, message) =>
        {
            confirmed++;
            Assert.Contains(skill.LocalizedName, message);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("msg_skill_permanent_note"), message);
            return System.Threading.Tasks.Task.FromResult(true);
        };
        engine.SuppressPopups = true;
        engine.Progress.LifetimeSkillPointsSpent = 99;
        tab.Purchase = id =>
        {
            bought++;
            Assert.Equal(skill.Id, id);
            Assert.False(card.IsEnabled);
            return System.Threading.Tasks.Task.FromResult((true, (string?)null, (long?)100));
        };
        var at = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), shell)!.Value;
        shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
        shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
        Assert.Equal(1, confirmed);
        Assert.Equal(1, bought);
        Assert.Equal(2, Bursts(shell));
        Assert.True(shell.PrestigeSweeping, $"host={shell.Named<Grid>("EventFxHost")?.Bounds}, transform={shell.Named<Border>("PrestigeSheen")?.RenderTransform}, active={shell.IsActive}");
        clock.Now += TimeSpan.FromSeconds(0.3).Ticks;
        BeatLoop.StepAll();
        Assert.Equal(0.16, shell.Named<Border>("PrestigeSheen")!.Opacity, 5);
        shell.StopEventFx();
        Assert.False(shell.PrestigeSweeping);
        Assert.Equal(0, shell.Named<Border>("PrestigeSheen")!.Opacity);
        Dispatcher.UIThread.RunJobs();

        tab.ConfirmPurchase = (_, _, _) => System.Threading.Tasks.Task.FromResult(false);
        tab.BuyAsync(card, skill).GetAwaiter().GetResult();
        Assert.Equal(1, bought);
        Assert.Equal(2, Bursts(shell));
        shell.ShowTab("home");
        Dispatcher.UIThread.RunJobs();
        shell.CelebrateEnhancementPurchase(new Border(), 2, null);
        Assert.Equal(3, Bursts(shell));
        AssertAt(shell, shell.NavAnchorForTab("enhancements")!);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.CelebrateEnhancementPurchase(new Border(), 1, tab.PrestigeAnchor);
        Assert.Equal(3, Bursts(shell));
        Assert.False(shell.PrestigeSweeping);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        shell.CelebrateEnhancementPurchase(new Border(), 1, null);
        Assert.True(shell.PrestigeSweeping);
        clock.Now += TimeSpan.FromSeconds(1.2).Ticks;
        BeatLoop.StepAll();
        Assert.False(shell.PrestigeSweeping);
        Assert.Equal(0, shell.Named<Border>("PrestigeSheen")!.Opacity);
        shell.CelebrateEnhancementPurchase(new Border(), 1, null);
        Assert.True(shell.PrestigeSweeping);
        shell.Close();
        Assert.False(shell.PrestigeSweeping);
    }, signedIn: true);

    [Theory]
    [InlineData("good_girl_streak")]
    [InlineData("oopsie_insurance")]
    [InlineData("pink_rush")]
    public void PurchaseAppliesImmediateSkillEffects(string id) => WithShell((shell, engine, _) =>
    {
        var s = CoreSettings.Current;
        s.SkillPoints = 100;
        s.UnlockedSkills = SkillDefinition.All.Where(x => x.Id != id).Select(x => x.Id).ToList();
        shell.ShowTab("enhancements");
        Dispatcher.UIThread.RunJobs();
        var tab = shell.Named<EnhancementsTabView>("EnhancementsTab")!;
        tab.ConfirmPurchase = (_, _, _) => System.Threading.Tasks.Task.FromResult(true);
        tab.Purchase = skillId =>
        {
            s.UnlockedSkills.Add(skillId);
            return System.Threading.Tasks.Task.FromResult((true, (string?)null, (long?)null));
        };
        var skill = SkillDefinition.All.Single(x => x.Id == id);
        int fixes = s.StreakFixCharges;
        try
        {
            tab.BuyAsync(tab, skill).GetAwaiter().GetResult();
            Assert.Equal(skill.Cost, s.SeasonPointsSpent);
            Assert.Equal(skill.Cost, engine.Progress.LifetimeSkillPointsSpent);
            if (id == "good_girl_streak")
            {
                Assert.Equal(1, s.StreakShieldsRemaining);
                Assert.Equal(DateTime.UtcNow.Date, s.LastStreakShieldResetDate);
            }
            if (id == "oopsie_insurance") Assert.Equal(fixes + 1, s.StreakFixCharges);
            if (id == "pink_rush") Assert.True(PinkRushHost.IsChecking);
        }
        finally { PinkRushHost.Stop(); }
    });

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public void PurchaseRaisesSkillBarkOnlyAfterSuccess(bool confirmed, bool success, int expected) => WithShell((shell, _, _) =>
    {
        CoreSettings.Current.SkillPoints = 100;
        var tab = shell.Named<EnhancementsTabView>("EnhancementsTab")!;
        var old = ConditioningControlPanel.Avalonia.Platform.BarkHead.Engine;
        var rule = new ConditioningControlPanel.Services.Bark.BarkRule
        {
            Id = "purchase-test", Trigger = "SkillUnlocked", VariantPool = new() { new("{skill}") },
            ClassRaw = "safety", // bypass cooldowns so duplicates cannot hide
        };
        var bark = new ConditioningControlPanel.Services.Bark.BarkEngine(() => new(new[] { rule }));
        var spoken = new List<string>();
        bark.Speak = speech => spoken.Add(speech.Text);
        bark.Start();
        ConditioningControlPanel.Avalonia.Platform.BarkHead.Engine = bark;
        try
        {
            tab.ConfirmPurchase = (_, _, _) => System.Threading.Tasks.Task.FromResult(confirmed);
            tab.Purchase = _ => System.Threading.Tasks.Task.FromResult((success, (string?)null, (long?)null));
            tab.BuyAsync(tab, SkillDefinition.All.Single(x => x.Id == "pink_hours")).GetAwaiter().GetResult();
            Assert.Equal(expected, spoken.Count);
            if (expected > 0) Assert.Equal("pink_hours", spoken.Single());
        }
        finally { ConditioningControlPanel.Avalonia.Platform.BarkHead.Engine = old; }
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

    private static void WithShell(Action<MainShellWindow, AchievementEngine, QuestService> body, bool signedIn = false) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-shell-event-fx-").FullName;
        var (oldSettings, oldAchievements, oldQuests) = (CoreSettings.ServiceProvider, AvApp.Achievements, AvApp.Quests);
        var oldTime = BeatLoop.Time;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        if (signedIn) CoreAccount.IsLoggedInProvider = () => true;
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
            BeatLoop.Time = oldTime;
            CoreAccount.IsLoggedInProvider = oldLogin;
            CoreSettings.ServiceProvider = oldSettings;
            AvApp.Quests = oldQuests;
            SetAchievements(oldAchievements);
            Directory.Delete(dir, true);
        }
    });

    private sealed class FxClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static void SetAchievements(AchievementEngine? engine) => typeof(AvApp)
        .GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, engine);
}
