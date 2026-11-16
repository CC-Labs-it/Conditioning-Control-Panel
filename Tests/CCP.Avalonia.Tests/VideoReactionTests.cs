using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using HeadApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class VideoReactionTests
{
    [Fact]
    public Task VideoLineAndPendingPanic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<HeadApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var oldAi = HeadApp.Ai;
        var oldVideo = CoreEngine.Video;
        var video = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance.Scheduler;
        CoreEngine.Video = video;
        var pathField = typeof(ConditioningControlPanel.Services.MandatoryVideoScheduler).GetField("<LastVideoPath>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var oldPath = pathField.GetValue(video);
        pathField.SetValue(video, "/tmp/spiral.mp4");
        var oldEnabled = s.AiChatEnabled;
        var oldAvatar = s.AvatarEnabled;
        var ai = new VideoAi();
        HeadApp.Ai = ai; s.AiChatEnabled = true; s.AvatarEnabled = true;
        // The panic quiet and engine hold are process-wide: put back whatever an earlier test left.
        var quiet = typeof(PanicAftermath).GetField("_quietUntilMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var held = typeof(PanicAftermath).GetField("_engineHeld", BindingFlags.NonPublic | BindingFlags.Static)!;
        var (oldQuiet, oldHeld) = (quiet.GetValue(null), held.GetValue(null));
        PanicAftermath.EndQuiet();
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            var finished = typeof(ConditioningControlPanel.Services.MandatoryVideoScheduler).GetField("VideoFinished", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ai.Reply = Task.FromResult<string?>("Finished");
            ((Action)finished.GetValue(video)!).Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("spiral", ai.Title);
            Assert.True(tube.HasBubbleUp);
            var panic = System.Linq.Enumerable.Single(PanicSurfaces.All, x => x.Id == "tube-speech");
            panic.Stop(shell); Dispatcher.UIThread.RunJobs();
            Assert.False(tube.HasBubbleUp);
            var generation = (int)tube.GetType().GetField("_videoReactionGeneration", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tube)!;
            var pending = new TaskCompletionSource<string?>(); ai.Reply = pending.Task;
            var work = tube.ReactToVideoAsync("next", generation);
            panic.Stop(shell); Dispatcher.UIThread.RunJobs();
            pending.SetResult("late"); await work;
            Assert.False(tube.HasBubbleUp);
            generation = (int)tube.GetType().GetField("_videoReactionGeneration", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tube)!;
            pending = new TaskCompletionSource<string?>(); ai.Reply = pending.Task;
            work = tube.ReactToVideoAsync("stop", generation);
            video.Stop(); pending.SetResult("late stop"); await work;
            Assert.False(tube.HasBubbleUp);
        }
        finally { shell.Close(); pathField.SetValue(video, oldPath); CoreEngine.Video = oldVideo; HeadApp.Ai = oldAi; s.AiChatEnabled = oldEnabled; s.AvatarEnabled = oldAvatar; quiet.SetValue(null, oldQuiet); held.SetValue(null, oldHeld); }
    });

    private sealed class VideoAi : IAiService
    {
        public string? Title;
        public Task<string?> Reply = Task.FromResult<string?>(null);
        public bool IsAvailable => true;
        public int DailyRequestsRemaining => 10;
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) { Title = title; return Reply; }
        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
