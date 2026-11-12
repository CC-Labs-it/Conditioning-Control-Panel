using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteLockCardQueueTests
{
    private sealed class VideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }

    private sealed class BubbleHost : IBubbleCountHost
    {
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) { }
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 0;
        public bool DeferTrigger(Action trigger) => LockCardWindow.DeferBubble(trigger);
    }

    [Fact]
    public async Task QueueTestRestoresTheFlashScheduler()
    {
        var wasRunning = CoreFlash.IsRunning;
        try
        {
            CoreFlash.Start();
            await RemoteCardWaitsForCardVideoAndBubbleCountAndTeardownDropsIt();
            Assert.True(CoreFlash.IsRunning);
        }
        finally { if (wasRunning) CoreFlash.Start(); else CoreFlash.Stop(); }
    }

    [Fact]
    public Task RemoteCardWaitsForCardVideoAndBubbleCountAndTeardownDropsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.LockCardPhrases, s.LockCardStrict, CoreLockCard.ShowHandler, CoreLockCard.CloseAllHandler,
            CoreEngine.Video, CoreEngine.BubbleCount);
        var flashWasRunning = CoreFlash.IsRunning;
        var video = new MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "/test/clip.mp4" })
            { DeferTrigger = LockCardWindow.DeferVideo };
        var bubbles = new BubbleCountScheduler(new BubbleHost());
        try
        {
            (s.LockCardPhrases, s.LockCardStrict) = (new Dictionary<string, bool> { ["GOOD GIRL"] = true }, false);
            (CoreLockCard.ShowHandler, CoreLockCard.CloseAllHandler) = (LockCardWindow.PostNext, LockCardWindow.ForceCloseAll);
            (CoreEngine.Video, CoreEngine.BubbleCount) = (video, bubbles);
            void RemoteCard() { Assert.Null(RemoteCommands.Execute("trigger_lock_card", null)); Dispatcher.UIThread.RunJobs(); }
            void CloseCard() { Assert.NotNull(LockCardWindow.Primary); LockCardWindow.Primary!.Close(); }

            RemoteCard();
            Assert.True(LockCardWindow.IsAnyOpen());
            RemoteCard(); RemoteCard(); // one pending replay, not one per request
            LockCardWindow.RetryPendingCard();
            Assert.True(LockCardWindow.IsAnyOpen());
            CloseCard();
            LockCardWindow.RetryPendingCard();
            Assert.True(LockCardWindow.IsAnyOpen());
            CloseCard();
            LockCardWindow.RetryPendingCard();
            Assert.False(LockCardWindow.IsAnyOpen());

            Assert.True(video.Trigger(path: "/test/clip.mp4"));
            RemoteCard();
            LockCardWindow.RetryPendingCard();
            Assert.False(LockCardWindow.IsAnyOpen());
            video.Stop();
            await Task.Delay(1500); // the real dispatcher timer replays after the video ends
            Dispatcher.UIThread.RunJobs();
            Assert.True(LockCardWindow.IsAnyOpen());
            CloseCard();

            bubbles.Trigger(forceTest: true);
            Assert.True(bubbles.IsBusy);
            RemoteCard();
            LockCardWindow.RetryPendingCard();
            Assert.False(LockCardWindow.IsAnyOpen());
            bubbles.ForceCleanup();
            LockCardWindow.RetryPendingCard();
            Assert.True(LockCardWindow.IsAnyOpen());
            CloseCard();

            // Video then card wait behind a card and replay in request order.
            RemoteCard();
            Assert.Null(RemoteCommands.Execute("trigger_video", null));
            RemoteCard();
            Assert.False(video.IsPlaying);
            CloseCard();
            LockCardWindow.RetryPendingCard();
            Assert.True(video.IsPlaying);
            Assert.False(LockCardWindow.IsAnyOpen());
            video.Stop();
            LockCardWindow.RetryPendingCard();
            Assert.True(LockCardWindow.IsAnyOpen());
            CloseCard();

            // Card then video wait behind a video, preserving the reverse order too.
            Assert.True(video.Trigger(path: "/test/clip.mp4"));
            RemoteCard();
            Assert.Null(RemoteCommands.Execute("trigger_video", null));
            video.Stop();
            LockCardWindow.RetryPendingCard();
            Assert.True(LockCardWindow.IsAnyOpen());
            Assert.False(video.IsPlaying);
            CloseCard();
            LockCardWindow.RetryPendingCard();
            Assert.True(video.IsPlaying);
            video.Stop();

            // Bubble count and video share that same pending order.
            RemoteCard();
            bubbles.Trigger(forceTest: true);
            Assert.Null(RemoteCommands.Execute("trigger_video", null));
            Assert.False(bubbles.IsBusy);
            CloseCard();
            LockCardWindow.RetryPendingCard();
            Assert.True(bubbles.IsBusy);
            Assert.False(video.IsPlaying);
            bubbles.ForceCleanup();
            LockCardWindow.RetryPendingCard();
            Assert.True(video.IsPlaying);
            video.Stop();

            RemoteCard();
            Assert.Null(RemoteCommands.Execute("trigger_video", null));
            CoreLockCard.CloseAll();
            LockCardWindow.RetryPendingCard();
            Assert.False(video.IsPlaying);

            Assert.True(video.Trigger(path: "/test/clip.mp4"));
            RemoteCard();
            CoreLockCard.CloseAll();
            video.Stop();
            LockCardWindow.RetryPendingCard();
            Assert.False(LockCardWindow.IsAnyOpen());
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            video.Stop();
            bubbles.ForceCleanup();
            if (flashWasRunning) CoreFlash.Start(); else CoreFlash.Stop();
            (s.LockCardPhrases, s.LockCardStrict, CoreLockCard.ShowHandler, CoreLockCard.CloseAllHandler,
                CoreEngine.Video, CoreEngine.BubbleCount) = saved;
        }
    });
}
