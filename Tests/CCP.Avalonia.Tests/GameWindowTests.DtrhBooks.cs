using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Models;
using Xunit;
using Head = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Dtrh_RunBooks_CreditOnceAndKeepCrashEvidenceOnTeardown() => RunDtrh(true, (_, _, _, _) =>
    {
        var property = typeof(Head).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = property.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("dtrh-books-").FullName;
        var sentinel = Path.Combine(CorePaths.UserData, "logs", "chaos_session.active");
        var oldSentinel = File.Exists(sentinel) ? File.ReadAllText(sentinel) : null;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        var oldQuests = Head.Quests;
        using var quests = new QuestService(null, dir);
        quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d") };
        property.SetValue(null, engine);
        Head.Quests = quests;
        try
        {
            var w = GameWindow.Launch("dtrh")!;
            var barks = new List<string>();
            w.DtrhRunStartedBark = d => barks.Add("start:" + d);
            w.DtrhRunCompletedBark = (_, d) => barks.Add("end:" + d);
            engine.Progress.TotalBubblesPopped = 95;
            int points = CoreSettings.Current.SkillPoints;
            w.HandleMessage("{\"type\":\"run-started\",\"difficulty\":\"Easy\"}");
            Assert.Contains("diff=Easy", File.ReadAllText(sentinel));
            const string end = "{\"type\":\"run-ended\",\"difficulty\":\"Easy\",\"durationSec\":180,\"sessionStats\":{\"bubblesPopped\":150}}";
            w.HandleMessage(end);
            w.HandleMessage(end);
            Assert.False(File.Exists(sentinel));
            Assert.Equal(245, engine.Progress.TotalBubblesPopped);
            Assert.Equal(150, quests.Progress.DailyQuests[0].CurrentProgress);
            Assert.Equal(points + 2, CoreSettings.Current.SkillPoints);
            Assert.Equal(new[] { "start:Easy", "end:Easy" }, barks);
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-progress\",\"durationSec\":10,\"sessionStats\":{\"bubblesPopped\":10}}");
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(File.Exists(sentinel));
            Assert.Equal(255, engine.Progress.TotalBubblesPopped);
            Assert.Equal(new[] { "start:Easy", "end:Easy", "start:Gentle" }, barks);
        }
        finally
        {
            Head.Quests = oldQuests;
            property.SetValue(null, previous);
            if (oldSentinel == null) ChaosCrashSentinel.Clear(); else File.WriteAllText(sentinel, oldSentinel);
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void Dtrh_ReportBug_OpensOwnedDialog() => RunDtrh(true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        w.HandleMessage("{\"type\":\"report-bug\"}");
        Dispatcher.UIThread.RunJobs();
        var report = Assert.Single(w.OwnedWindows.OfType<BugReportWindow>());
        Assert.True(report.IsVisible);
        report.Close();
        w.Close();
    });
}
