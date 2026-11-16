using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Dtrh_RunBooks_BarkOnceAndKeepCrashEvidenceOnTeardown() => RunDtrh(true, (_, _, _, _) =>
    {
        var sentinel = Path.Combine(CorePaths.UserData, "logs", "chaos_session.active");
        var oldSentinel = File.Exists(sentinel) ? File.ReadAllText(sentinel) : null;
        try
        {
            var w = GameWindow.Launch("dtrh")!;
            var barks = new List<string>();
            w.DtrhRunStartedBark = d => barks.Add("start:" + d);
            w.DtrhRunCompletedBark = (_, d) => barks.Add("end:" + d);
            w.HandleMessage("{\"type\":\"run-started\",\"difficulty\":\"Easy\"}");
            Assert.Contains("diff=Easy", File.ReadAllText(sentinel));
            const string end = "{\"type\":\"run-ended\",\"difficulty\":\"Easy\",\"durationSec\":180}";
            w.HandleMessage(end);
            w.HandleMessage(end);
            Assert.False(File.Exists(sentinel));
            Assert.Equal(new[] { "start:Easy", "end:Easy" }, barks);
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-progress\",\"durationSec\":10}");
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(File.Exists(sentinel));
            Assert.Equal(new[] { "start:Easy", "end:Easy", "start:Gentle" }, barks);
        }
        finally
        {
            if (oldSentinel == null) ChaosCrashSentinel.Clear(); else File.WriteAllText(sentinel, oldSentinel);
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
