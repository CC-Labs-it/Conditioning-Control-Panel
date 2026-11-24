using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class TubeServiceReactionTests
{
    private sealed class VideoHost : IMandatoryVideoHost
    {
        public int FloatingLimit => 1;
        public int FloatingUp => 0;
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task VideoAnnouncementShowsAndStopClearsIt(bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            AvatarTubeWindow.TestLive = tube;
            Assert.True(video.Trigger(path: "/video.mp4"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(tube.HasBubbleUp);
            if (panic) PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(null!);
            else video.ForceCleanup();
            Assert.False(tube.HasBubbleUp);
            video.ForceCleanup();
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);
            tube.Close();
            Assert.True(video.Trigger(path: "/video.mp4"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);
        }
        finally { video.ForceCleanup(); tube.Close(); AvatarTubeWindow.TestLive = null; CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoEndDoesNotSilenceLaterSpeech() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var freeze = CoreSubliminal.BambiFreezeProvider;
        CoreSubliminal.BambiFreezeProvider = null;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/video.mp4" });
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.AttachServiceReactions(null, video);
            Assert.True(video.Trigger(path: "/video.mp4"));
            Assert.True(tube.HasBubbleUp);
            tube.EndSpeechBubble();
            tube.GigglePriority("another line", aiGenerated: false);
            Assert.True(tube.HasBubbleUp);
            video.ForceCleanup();
            Assert.True(tube.HasBubbleUp);
        }
        finally { video.ForceCleanup(); tube.Close(); CoreSubliminal.BambiFreezeProvider = freeze; }
        return Task.CompletedTask;
    });

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
