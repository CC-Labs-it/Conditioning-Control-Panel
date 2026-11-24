using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>PopQuizHost: the Test button path opens a quiz, a lock card defers it until the card
/// closes, and CoreEngine.Stop closes an open one.</summary>
public sealed class PopQuizHostTests
{
    [Fact]
    public Task OpensDefersBehindLockCardAndClosesOnEngineStop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var scheduler = PopQuizHost.Instance.Scheduler;
        var wasEnabled = CoreSettings.Current.PopQuizEnabled;
        CoreEngine.PopQuiz = scheduler;
        try
        {
            CoreSettings.Current.PopQuizEnabled = true;
            scheduler.Start();
            scheduler.Show(isTest: true);
            Assert.True(PopQuizWindow.IsAnyOpen());
            CoreEngine.Stop();
            Assert.False(PopQuizWindow.IsAnyOpen());

            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            scheduler.Show(isTest: true);
            Assert.False(PopQuizWindow.IsAnyOpen());   // deferred, not stacked on the card
            LockCardWindow.Primary!.Close();
            Dispatcher.UIThread.RunJobs();
            LockCardWindow.RetryPendingCard();
            Assert.True(PopQuizWindow.IsAnyOpen());    // replayed once the card left

            // #763 the other way: a card waits behind an open quiz and replays when it closes.
            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            Assert.False(LockCardWindow.IsAnyOpen());
            PopQuizWindow.ForceCloseAll();
            Assert.True(LockCardWindow.IsAnyOpen());
            LockCardWindow.ForceCloseAll();

            // Audit #1833: a card held behind a quiz must not flash up while the engine stops.
            scheduler.Start();
            scheduler.Show(isTest: true);
            Assert.True(PopQuizWindow.IsAnyOpen());
            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            Assert.False(LockCardWindow.IsAnyOpen());   // held
            CoreEngine.Stop();
            Dispatcher.UIThread.RunJobs();
            Assert.False(PopQuizWindow.IsAnyOpen());
            Assert.False(LockCardWindow.IsAnyOpen());
        }
        finally
        {
            PopQuizWindow.ForceCloseAll();
            LockCardWindow.ForceCloseAll();
            CoreEngine.PopQuiz = null;
            CoreSettings.Current.PopQuizEnabled = wasEnabled;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task QuizWaitsOnceAndPanicDropsIt(bool videoBusy, bool drop) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var bubble = new BubbleCountScheduler(BubbleCountHost.Instance, TimeProvider.System, () => System.Array.Empty<string>());
        var video = new MandatoryVideoScheduler(
            global::ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance,
            TimeProvider.System, () => new[] { "/none/clip.mp4" });
        var (oldBubble, oldVideo, oldFreeze) = (CoreEngine.BubbleCount, CoreEngine.Video, CoreSubliminal.BambiFreezeProvider);
        CoreEngine.BubbleCount = bubble;
        CoreEngine.Video = video;
        CoreSubliminal.BambiFreezeProvider = () => { };
        try
        {
            if (videoBusy)
                typeof(MandatoryVideoScheduler).GetField("_playing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(video, true);
            else
                bubble.Trigger(forceTest: true);
            Assert.True(PopQuizHost.Instance.IsInteractionBusy);
            var scheduler = PopQuizHost.Instance.Scheduler;
            scheduler.Show(isTest: true);
            scheduler.Show(isTest: true);
            Assert.False(PopQuizWindow.IsAnyOpen());
            if (drop) PopQuizHost.Instance.CloseAll();
            if (videoBusy)
                typeof(MandatoryVideoScheduler).GetField("_playing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(video, false);
            else bubble.ForceCleanup();
            LockCardWindow.RetryPendingCard();
            Assert.Equal(!drop, PopQuizWindow.IsAnyOpen());
            PopQuizWindow.ForceCloseAll();
            LockCardWindow.RetryPendingCard();
            Assert.False(PopQuizWindow.IsAnyOpen());
        }
        finally
        {
            PopQuizHost.Instance.CloseAll();
            LockCardWindow.ForceCloseAll();
            bubble.ForceCleanup();
            (CoreEngine.BubbleCount, CoreEngine.Video, CoreSubliminal.BambiFreezeProvider) = (oldBubble, oldVideo, oldFreeze);
        }
        return Task.CompletedTask;
    });

    private sealed class ResultHost : IBubbleCountHost
    {
        public void Show(string path, int difficulty, bool strict, Action<bool> complete) { }
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 0;
    }

    [Fact]
    public Task AutonomyWaitsDuringBubbleResult() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var bubble = new BubbleCountScheduler(new ResultHost(), TimeProvider.System, () => System.Array.Empty<string>());
        var (oldBubble, oldFreeze, oldStrict) = (CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider, CoreSettings.Current.BubbleCountStrictLock);
        CoreEngine.BubbleCount = bubble;
        CoreSubliminal.BambiFreezeProvider = () => { };
        CoreSettings.Current.BubbleCountStrictLock = true;
        var shell = new MainShellWindow();
        try
        {
            bubble.Trigger(forceTest: true);
            bubble.OnComplete(false);
            Assert.False(BubbleCountWindow.IsAnyOpen());
            Assert.True(bubble.IsBusy);
            Assert.True(shell.Autonomy.IsBusy());
            bubble.ForceCleanup();
            Assert.False(shell.Autonomy.IsBusy());
        }
        finally
        {
            shell.Close();
            bubble.ForceCleanup();
            (CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider, CoreSettings.Current.BubbleCountStrictLock) = (oldBubble, oldFreeze, oldStrict);
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF 7.1.5 (f154640bb): the card's "Turn these off" link switches PopQuizEnabled off,
    /// keeps the Graded Intake switch in step, closes like Esc and stops the schedule.</summary>
    [Theory]
    [InlineData(false)]   // Enter on the focused link
    [InlineData(true)]    // left click (WPF MouseLeftButtonUp)
    public Task TurnOffLinkSwitchesQuizzesOffAndSyncsTheIntakeSwitch(bool byMouse) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var scheduler = PopQuizHost.Instance.Scheduler;
        var wasEnabled = CoreSettings.Current.PopQuizEnabled;
        CoreEngine.PopQuiz = scheduler;
        var tab = new global::ConditioningControlPanel.Avalonia.Views.Tabs.GradedIntakeTabView();
        var host = new global::Avalonia.Controls.Window { Content = tab };
        try
        {
            CoreSettings.Current.PopQuizEnabled = true;
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var chk = tab.FindControl<global::Avalonia.Controls.CheckBox>("ChkPopQuizEnabled")!;
            Assert.True(chk.IsChecked);
            scheduler.Start();
            var quiz = new PopQuizWindow(PopQuizScheduler.QuestionPool[0], isTest: true);
            quiz.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(PopQuizWindow.IsAnyOpen());

            var link = quiz.FindControl<global::Avalonia.Controls.TextBlock>("TxtTurnOff")!;
            Assert.True(link.Focusable);
            if (byMouse)
            {
                var at = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), quiz)!.Value;
                quiz.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
                quiz.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
            }
            else
                link.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                {
                    RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
                    Key = global::Avalonia.Input.Key.Enter,
                });
            Dispatcher.UIThread.RunJobs();

            Assert.False(CoreSettings.Current.PopQuizEnabled);
            Assert.False(PopQuizWindow.IsAnyOpen());
            Assert.False(scheduler.IsRunning);
            Assert.False(chk.IsChecked);
        }
        finally
        {
            host.Close();
            PopQuizWindow.ForceCloseAll();
            scheduler.Stop();
            CoreEngine.PopQuiz = null;
            CoreSettings.Current.PopQuizEnabled = wasEnabled;
        }
        return Task.CompletedTask;
    });
}
