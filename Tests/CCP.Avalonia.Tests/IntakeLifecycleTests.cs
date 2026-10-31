using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The intake window's lifecycle through the opener Begin Intake uses
/// (WPF IntakeHostService.Launch / DuckMainWindow / ApplyHostFullscreen / StartHeartbeatWatch / Recover).
/// Page messages are fed through HandleMessage; time is a stepped clock.</summary>
public sealed class IntakeLifecycleTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static IntakeHostWindow Only() => Assert.Single(IntakeHostWindow.Snapshot());

    private static async Task WithCleanState(Func<Task> body)
    {
        var fullscreen = CoreSettings.Current.IntakeFullscreen;
        try { await AvaloniaTestDispatcher.RunAsync(() => { Setup(); return body(); }); }
        finally
        {
            await AvaloniaTestDispatcher.RunAsync(() => { IntakeHostWindow.CloseAllForPanic(); return Task.CompletedTask; });
            IntakeHostWindow.RelaunchedOnce = false;
            CoreSettings.Current.IntakeFullscreen = fullscreen;
        }
    }

    [Fact]
    public Task NativeDeathBeforeReadyRetriesWindowedOnceAndIgnoresClosedEngines() => WithCleanState(() =>
    {
        IntakeHostWindow.RelaunchedOnce = false;
        CoreSettings.Current.IntakeFullscreen = true;
        var first = GradedIntakeTabView.OpenIntake(null, duckMain: false);
        first.Web.OnProcessFailed("RenderProcessExited"); // a crash during boot has no heartbeat yet
        var second = Only();
        Assert.NotSame(first, second);
        Assert.NotEqual(WindowState.FullScreen, second.WindowState);
        Assert.True(CoreSettings.Current.IntakeFullscreen); // recovery does not overwrite the preference
        first.Web.OnProcessFailed("late");
        Assert.Same(second, Only());
        second.Web.OnProcessFailed("RenderProcessExited");
        Assert.Empty(IntakeHostWindow.Snapshot());
        return Task.CompletedTask;
    });

    [Fact]
    public Task NativeRecoveryKeepsDuckChoiceAndRestoresMainOnSecondDeath() => WithCleanState(() =>
    {
        IntakeHostWindow.RelaunchedOnce = false;
        var main = new Window();
        main.Show();
        try
        {
            var first = GradedIntakeTabView.OpenIntake(main, duckMain: true);
            first.Web.OnProcessFailed("webkit:0");
            var second = Only();
            Assert.NotSame(first, second);
            Assert.Equal(WindowState.Minimized, main.WindowState);
            second.Web.OnProcessFailed("webkit:0");
            Assert.Empty(IntakeHostWindow.Snapshot());
            Assert.NotEqual(WindowState.Minimized, main.WindowState);
        }
        finally { main.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void NativeSubscriptionQueuesDeathAndDropsStaleCallbacksOnDispose()
    {
        var queued = new Queue<Action>();
        var reasons = new List<string>();
        Action<string>? native = null;
        int stops = 0;
        var watch = new global::ConditioningControlPanel.Avalonia.Platform.WebProcessWatch.Watch(reasons.Add, queued.Enqueue);
        watch.Listen(cb => { native = cb; return () => stops++; });
        native!("first");
        Assert.Empty(reasons); // native event stack must unwind before destroying its engine
        queued.Dequeue()();
        Assert.Equal(new[] { "first" }, reasons);
        native("late");
        watch.Dispose();
        watch.Dispose();
        queued.Dequeue()();
        Assert.Equal(new[] { "first" }, reasons);
        Assert.Equal(1, stops);
    }

    [Fact]
    public Task TheRunDucksTheControlPanelAndEveryCloseGivesItBack() => WithCleanState(() =>
    {
        var main = new Window();
        main.Show();
        main.WindowState = WindowState.Maximized;

        var run = GradedIntakeTabView.OpenIntake(main, duckMain: true);
        Assert.Equal(WindowState.Minimized, main.WindowState);
        Assert.True(Only().HeartbeatWatchRunning);
        Assert.Same(run, GradedIntakeTabView.OpenIntake(main, duckMain: true)); // one live run: focused, not doubled
        Assert.Same(run, Only());

        IntakeHostWindow.CloseAllForPanic();                       // panic is one of the close paths
        Assert.Empty(IntakeHostWindow.Snapshot());
        Assert.Equal(WindowState.Maximized, main.WindowState);     // a maximised panel comes back maximised

        GradedIntakeTabView.OpenIntake(main, duckMain: false);     // first-ever run: no duck
        Assert.Equal(WindowState.Maximized, main.WindowState);
        main.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task BeginUsesUnsavedIntakeHistoryToDuck() => WithCleanState(() =>
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("intake-history-");
        using var card = new ConditioningControlPanel.Services.IntakePunchCardService(
            System.IO.Path.Combine(dir.FullName, "card.json"));
        var previous = AvApp.IntakePunchCard;
        AvApp.IntakePunchCard = card;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.HasPremiumAccessProvider = () => true;
        CoreAi.IsAvailableProvider = () => true;
        var view = new GradedIntakeTabView();
        var main = new Window { Content = view };
        main.Show();
        try
        {
            view.BtnStartIntake_Click(null, new global::Avalonia.Interactivity.RoutedEventArgs());
            Assert.NotEqual(WindowState.Minimized, main.WindowState);
            Only().Close();
            card.NotifyIntakeCompleted("draft"); // pending save: the live service must win over disk
            Assert.False(System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "card.json")));
            view.BtnStartIntake_Click(null, new global::Avalonia.Interactivity.RoutedEventArgs());
            Assert.Equal(WindowState.Minimized, main.WindowState);
        }
        finally
        {
            IntakeHostWindow.CloseAllForPanic();
            main.Close();
            AvApp.IntakePunchCard = previous;
            card.Dispose();
            dir.Delete(true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FullscreenSetEchoesTheRealModeAndTheNextLaunchRemembersIt() => WithCleanState(() =>
    {
        CoreSettings.Current.IntakeFullscreen = false;
        var first = GradedIntakeTabView.OpenIntake(null, duckMain: false);
        Assert.NotEqual(WindowState.FullScreen, first.WindowState);
        var sent = new List<JObject>();
        first.Posted += json => sent.Add(JObject.Parse(json));

        first.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Assert.Equal(WindowState.FullScreen, first.WindowState);
        Assert.True((bool)sent.Single(m => (string?)m["type"] == "fullscreen")["on"]!);
        Assert.True(CoreSettings.Current.IntakeFullscreen);
        first.Close();

        var second = GradedIntakeTabView.OpenIntake(null, duckMain: false);
        Assert.Equal(WindowState.FullScreen, second.WindowState);
        second.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Assert.NotEqual(WindowState.FullScreen, second.WindowState);
        Assert.False(CoreSettings.Current.IntakeFullscreen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ASilentPageRelaunchesOnceWindowedThenGivesUp() => WithCleanState(() =>
    {
        IntakeHostWindow.RelaunchedOnce = false;
        CoreSettings.Current.IntakeFullscreen = true;
        var main = new Window();
        main.Show();
        var first = GradedIntakeTabView.OpenIntake(main, duckMain: true);
        var clock = new SteppedClock();
        first.Clock = clock;

        clock.Now += TimeSpan.FromMinutes(5);
        first.CheckHeartbeat();                                     // still loading: never trips
        Assert.Same(first, Only());

        first.HandleMessage("{\"type\":\"ready\"}");
        clock.Now += TimeSpan.FromSeconds(15);
        first.HandleMessage("{\"type\":\"heartbeat\"}");
        clock.Now += TimeSpan.FromSeconds(19);
        first.CheckHeartbeat();
        Assert.Same(first, Only());                                 // beats keep it alive

        clock.Now += TimeSpan.FromSeconds(2);
        first.CheckHeartbeat();
        var second = Only();
        Assert.NotSame(first, second);                              // relaunched...
        Assert.NotEqual(WindowState.FullScreen, second.WindowState); // ...windowed, despite the remembered mode
        Assert.Equal(WindowState.Minimized, main.WindowState);      // and the duck choice carried

        second.Clock = clock;
        second.HandleMessage("{\"type\":\"ready\"}");
        clock.Now += TimeSpan.FromSeconds(21);
        second.CheckHeartbeat();
        Assert.Empty(IntakeHostWindow.Snapshot());                  // second wedge: gives up
        Assert.NotEqual(WindowState.Minimized, main.WindowState);   // and hands the panel back
        main.Close();
        return Task.CompletedTask;
    });
}
