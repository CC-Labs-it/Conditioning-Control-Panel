using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class TubeServiceReactionTests
{
    [Fact]
    public Task AchievementReactionUsesWpfLineAndStopsOnClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        var lines = new List<string>();
        var tube = new AvatarTubeWindow(null) { AchievementReactionSink = lines.Add };
        try
        {
            tube.Show();
            tube.AttachServiceReactions(engine);
            Assert.True(engine.TryUnlock("pop_the_thought"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { $"Achievement unlocked: {Achievement.All["pop_the_thought"].Name}! *giggles*" }, lines);
            tube.Close();
            var handlers = (Delegate?)typeof(AchievementEngine).GetField("Unlocked", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
            Assert.Null(handlers);
            Assert.True(engine.TryUnlock("relapse"));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(lines);
        }
        finally
        {
            tube.Close();
            Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    });
}
