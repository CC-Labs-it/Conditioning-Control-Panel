using System;
using System.Linq;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using IconKind = FluentIcons.Common.Symbol;
using IconVariant = FluentIcons.Common.IconVariant;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The head's session run (WPF MainWindow.Presets.cs StartSession / OnSessionStarted /
/// OnSessionStopped / OnSessionLogReady): a started session locks the dials and shows its clock,
/// Stop everything pauses it (the panic tail), the pause button resumes it, and a stop brings the dials back
/// and opens the Ended Early recap by itself.</summary>
public sealed class SessionRunWireTests
{
    [Fact]
    public Task Remote_resume_refuses_local_panic_and_resumes_other_pauses() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = new MainShellWindow();
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var relay = RemoteControlTabView.Relay.Value;
        try
        {
            runner.Start(new Session { Id = "remote_resume", Name = "Resume", DurationMinutes = 60 });
            runner.Pause();
            shell.NoteLocalPanicPause();
            Assert.Equal("panic", relay.ResumeSession!(() => true));
            Assert.True(runner.IsPaused);
            runner.Resume();
            runner.Pause();
            Assert.Null(relay.ResumeSession!(() => true));
            Assert.False(runner.IsPaused);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            AvApp.Sessions = null;
            shell.Close();
        }
        Assert.Null(relay.ResumeSession);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task SessionConfirmCopyAndPauseOptOut()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var previous = AvApp.Sessions;
            var oldSkip = CoreSettings.Current.SkipPauseXpWarning;
            var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                CoreSettings.Current.SkipPauseXpWarning = false;
                shell.Show();
                LocalizationManager.Instance.SetLanguage("fr");
                var session = new Session { Id = "morning_drift", Name = "Different authored name", IsAvailable = true, DurationMinutes = 60 };
                Assert.NotEqual(session.Name, session.GetModeAwareName());
                shell.BtnStartSession_Click(session);
                var start = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal($"🌅 Start {session.GetModeAwareName()}?", start.Title);
                var startLabel = Assert.IsType<IconLabel>(start.FindControl<Button>("BtnOk")!.Content);
                Assert.Equal("▶ Start Session", startLabel.Source);
                Assert.NotEqual(Loc.Get("btn_start_session"), startLabel.Source); // WPF copy stays English.
                Assert.Equal("Start Session", startLabel.Label.Text);
                var startGlyph = Assert.Single(startLabel.Children.OfType<IconGlyph>());
                Assert.Equal(IconKind.Play, startGlyph.Kind);
                Assert.Equal(IconVariant.Filled, startGlyph.Variant);
                Assert.False(startGlyph.IsFallback);
                Assert.Equal("Not yet", Assert.IsType<TextBlock>(start.FindControl<Button>("BtnCancel")!.Content).Text);
                // WPF ShowStyledDialog has no default button or initial button focus.
                foreach (var key in new[] { Key.Enter, Key.Space })
                {
                    start.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Space, null);
                    Assert.True(start.IsVisible);
                    Assert.False(runner.IsRunning);
                    start.KeyRelease(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Space, null);
                    Assert.True(start.IsVisible);
                    Assert.False(runner.IsRunning);
                }
                start.FindControl<Button>("BtnCancel")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Yield();
                LocalizationManager.Instance.SetLanguage(oldLanguage);
                runner.Start(session);
                var stopTask = shell.ConfirmStopSession("title_stop_session_confirm", "msg_stop_session_body", "btn_yes_stop_session", "btn_keep_going");
                var stop = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Contains(session.GetModeAwareName(), stop.FindControl<TextBlock>("TxtMessage")!.Text);
                stop.FindControl<Button>("BtnCancel")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await stopTask;
                var pause = shell.Named<Button>("BtnPauseSession")!;
                foreach (var yes in new[] { false, true })
                {
                    pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                    Assert.Equal(Loc.Get("btn_yes_pause"), Assert.IsType<TextBlock>(dialog.FindControl<Button>("BtnOk")!.Content).Text);
                    Assert.Equal(Loc.Get("btn_keep_going"), Assert.IsType<TextBlock>(dialog.FindControl<Button>("BtnCancel")!.Content).Text);
                    var box = dialog.FindControl<CheckBox>("DontAskAgain")!;
                    Assert.True(box.IsVisible);
                    box.IsChecked = true;
                    dialog.FindControl<Button>(yes ? "BtnOk" : "BtnCancel")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Yield();
                    Assert.Equal(yes, CoreSettings.Current.SkipPauseXpWarning);
                    Assert.Equal(yes, runner.IsPaused);
                }
                runner.Resume();
                pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.True(runner.IsPaused);
                Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
            }
            finally
            {
                foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
                runner.Stop();
                CoreEngine.Stop();
                CoreSettings.Current.SkipPauseXpWarning = oldSkip;
                LocalizationManager.Instance.SetLanguage(oldLanguage);
                CoreSettings.Save();
                AvApp.Sessions = previous;
                shell.Close();
            }
        });
    }

    [Theory]
    [InlineData(true, "Yes, stop session")] // WPF MainWindow.Presets.cs:2098
    [InlineData(false, "Stop the session")] // WPF MainWindow.StartStop.cs:85
    public async Task StopConfirmUsesWpfCopyAndNoKeepsTheSessionRunning(bool presets, string yesText)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                shell.Show();
                var session = new Session { Id = "stop_copy", Name = "Stop copy", DurationMinutes = 60 };
                runner.Start(session);
                foreach (var answer in new[] { "no", "enter-no", "close", "yes" })
                {
                    // Drive both entry points so swapped or shared keys cannot hide in the helper.
                    if (presets) shell.BtnStartSession_Click(null);
                    else shell.Named<Button>("BtnStart")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                    var no = dialog.FindControl<Button>("BtnCancel")!;
                    var yes = dialog.FindControl<Button>("BtnOk")!;
                    Assert.Equal("Keep going", Assert.IsType<TextBlock>(no.Content).Text);
                    Assert.Equal(yesText, Assert.IsType<TextBlock>(yes.Content).Text);
                    Assert.False(no.IsDefault || yes.IsDefault || no.IsCancel || yes.IsCancel);
                    // WPF ShowStyledDialog does not focus Yes: stray keys cannot end a session.
                    dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Assert.True(dialog.IsVisible, "Enter on opening must leave the stop dialog open");
                    dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Assert.True(dialog.IsVisible, "Releasing Enter must leave the stop dialog open");
                    Assert.True(runner.IsRunning);
                    dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                    Assert.True(dialog.IsVisible, "Space on opening must leave the stop dialog open");
                    dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                    Assert.True(dialog.IsVisible, "Releasing Space must leave the stop dialog open");
                    Assert.True(runner.IsRunning);
                    dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Assert.True(dialog.IsVisible);
                    Assert.True(runner.IsRunning);
                    if (answer == "enter-no")
                    {
                        no.Focus();
                        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    }
                    else if (answer == "close") dialog.Close();
                    else (answer == "no" ? no : yes).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    // The async-void entry point must settle before judging its result.
                    var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Dispatcher.UIThread.Post(() => settled.SetResult(), DispatcherPriority.Background);
                    await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(dialog.IsVisible);
                    Assert.Equal(answer != "yes", runner.IsRunning);
                    if (answer != "yes")
                    {
                        Assert.Same(session, runner.CurrentSession);
                        Assert.False(runner.IsPaused);
                    }
                }
            }
            finally
            {
                foreach (var dialog in shell.OwnedWindows.ToArray()) dialog.Close();
                await Task.Yield(); // drain confirmation continuations even when an assertion fails
                runner.Stop();
                CoreEngine.Stop();
                AvApp.Sessions = null;
                CoreSession.IsSessionRunningProvider = null;
                shell.Close();
            }
        });
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Bottom_bar_projection_restores_caller_state(bool countdown) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var previousSessions = AvApp.Sessions;
        var previousCountdown = CoreSettings.Current.ShowSessionCountdown;
        var sentinel = new SessionRunner(new SessionLogService());
        try
        {
            AvApp.Sessions = sentinel;
            CoreSettings.Current.ShowSessionCountdown = countdown;
            // No isolation hook runs between this call and the assertions.
            await Bottom_bar_projection_uses_the_runner_clock_without_a_shell();
            Assert.Equal(countdown, CoreSettings.Current.ShowSessionCountdown);
            Assert.Same(sentinel, AvApp.Sessions);
        }
        finally
        {
            AvApp.Sessions = previousSessions;
            CoreSettings.Current.ShowSessionCountdown = previousCountdown;
        }
    });

    [Fact]
    public Task Bottom_bar_projection_uses_the_runner_clock_without_a_shell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousSessions = AvApp.Sessions;
        var previousCountdown = CoreSettings.Current.ShowSessionCountdown;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var status = new TextBlock { IsVisible = false };
        try
        {
            CoreEngine.Start();
            MainShellWindow.RefreshBarStatus(status);
            Assert.True(status.IsVisible);
            Assert.Equal(Loc.Get("label_running"), status.Text);
            CoreSettings.Current.ShowSessionCountdown = true;
            runner.Start(new Session { Id = "bar-projection", Name = "Clock", DurationMinutes = 10 });
            runner.Pause(); // Freeze the clock for deterministic label comparisons.
            MainShellWindow.RefreshBarStatus(status);
            var mark = " [" + Loc.Get("label_paused") + "]";
            Assert.StartsWith("Clock - 09:", status.Text);
            Assert.EndsWith(" left" + mark, status.Text);
            runner.CurrentSession!.DurationMinutes = 0;
            MainShellWindow.RefreshBarStatus(status);
            Assert.EndsWith(" elapsed" + mark, status.Text);
            CoreSettings.Current.ShowSessionCountdown = false;
            MainShellWindow.RefreshBarStatus(status);
            Assert.Equal("Clock" + mark, status.Text);
            runner.Resume();
            MainShellWindow.RefreshBarStatus(status);
            Assert.Equal("Clock", status.Text);
            runner.Stop();
            MainShellWindow.RefreshBarStatus(status);
            Assert.Equal("", status.Text);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            AvApp.Sessions = previousSessions;
            CoreSettings.Current.ShowSessionCountdown = previousCountdown;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Bottom_bar_tracks_session_pause_countdown_preference_and_stop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousSessions = AvApp.Sessions;
        var previousStopped = CoreEngine.StoppedHook;
        var previousCountdown = CoreSettings.Current.ShowSessionCountdown;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var shell = new MainShellWindow();
        try
        {
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            // WPF Presets.cs:1899: the engine alone (no session) shows the running label; Stop clears it.
            CoreEngine.Start();
            shell.UpdateStartButton();
            Assert.Equal(Loc.Get("label_running"), shell.Named<TextBlock>("TxtPresetsStatus")!.Text);
            CoreEngine.Stop();
            Assert.Equal("", shell.Named<TextBlock>("TxtPresetsStatus")!.Text);
            CoreSettings.Current.ShowSessionCountdown = true;
            shell.StartSession(new Session { Id = "bar-clock", Name = "Bar clock", DurationMinutes = 10 });
            var status = shell.Named<TextBlock>("TxtPresetsStatus")!;
            Assert.StartsWith("Bar clock - ", status.Text);
            Assert.EndsWith(" left", status.Text);
            status.IsVisible = false;
            runner.CurrentSession!.DurationMinutes = 0;
            shell.OnSessionTick();
            Assert.True(status.IsVisible);
            Assert.EndsWith(" elapsed", status.Text);
            runner.CurrentSession!.DurationMinutes = 10;
            runner.Pause();
            shell.SetPauseButton(true); // A held tick must still repaint the mark immediately.
            Assert.EndsWith(" [" + Loc.Get("label_paused") + "]", status.Text);
            CoreSettings.Current.ShowSessionCountdown = false;
            shell.OnSessionTick();
            Assert.Equal("Bar clock [" + Loc.Get("label_paused") + "]", status.Text);
            runner.Resume();
            shell.SetPauseButton(false);
            Assert.Equal("Bar clock", status.Text);
            runner.Stop();
            Assert.Equal("", status.Text);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            CoreEngine.StoppedHook = previousStopped;
            AvApp.Sessions = previousSessions;
            CoreSettings.Current.ShowSessionCountdown = previousCountdown;
            shell.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task StartLocksAndLabelsTrayStopPausesResumeRestartsStopOpensTheRecap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            runner.SessionLog.LogReady += shell.OnSessionLogReady;
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                var session = new Session { Id = "wire_test", Name = "Wire Test", Icon = "🧪", DurationMinutes = 1 };
                var startBackground = shell.Named<Button>("BtnStart")!.Background;
                shell.StartSession(session);

                Assert.True(runner.IsRunning);
                Assert.True(CoreEngine.IsRunning);
                Assert.True(shell.IsSessionFeatureLockActive);   // seeded by the runner, not the engine
                Assert.StartsWith("Wire Test 00:", shell.Named<TextBlock>("TxtStartLabel")!.Text);
                var presets = shell.Named<PresetsTabView>("PresetsTab")!;
                var stopLabel = Assert.IsType<TextBlock>(presets.BtnStartSession.Content).Text!;
                Assert.StartsWith("STOP SESSION (00:", stopLabel);

                MainShellWindow.StopEverything();   // the tray's Stop everything (and the panic tail)
                // WPF MainWindow.xaml.cs:1726: the session is paused, not ended; the engine stops.
                Assert.True(runner.IsRunning && runner.IsPaused);
                Assert.Equal(1, runner.PauseCount);
                Assert.False(CoreEngine.IsRunning || CoreFlash.IsRunning);
                Assert.True(shell.IsSessionFeatureLockActive);
                var pause = shell.Named<Button>("BtnPauseSession")!;
                Assert.True(pause.IsVisible);
                Assert.Equal("\u25B6", shell.Named<TextBlock>("TxtPauseIcon")!.Text);
                Assert.Equal(Loc.Get("tooltip_resume_session"), ToolTip.GetTip(pause));

                Assert.Equal(Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
                Assert.Equal("▶", shell.Named<TextBlock>("TxtStartIcon")!.Text);
                Assert.Same(startBackground, shell.Named<Button>("BtnStart")!.Background);
                Assert.Contains(Loc.Get("label_paused"), Assert.IsType<TextBlock>(presets.BtnStartSession.Content).Text);
                pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));   // Resume
                Assert.True(CoreEngine.IsRunning);
                Assert.False(runner.IsPaused);
                Assert.Equal("\u23F8", shell.Named<TextBlock>("TxtPauseIcon")!.Text);
                Assert.Equal(Loc.GetF("tooltip_pause_session_100_xp_penalty_per_pause_npause", 1), ToolTip.GetTip(pause));

                MainShellWindow.StopEverything();
                var runs = CoreSettings.Current.TotalSessions;
                shell.Named<Button>("BtnStart")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.True(CoreEngine.IsRunning);
                Assert.False(runner.IsPaused);
                Assert.Equal(runs, CoreSettings.Current.TotalSessions);

                System.Action? pendingResume = null;
                shell.ResumeEffect = start => pendingResume = start;
                MainShellWindow.StopEverything();
                pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.NotNull(pendingResume);
                MainShellWindow.StopEverything(); // a newer panic overtakes the queued Resume
                pendingResume!();
                Assert.False(CoreEngine.IsRunning || CoreFlash.IsRunning);
                Assert.True(runner.IsPaused && ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced());
                shell.ResumeEffect = MainShellWindow.StartEffect;
                shell.StartEngineFromUser();

                runner.Stop(completed: false);   // the stop-session confirm's OK
                Assert.False(runner.IsRunning || CoreEngine.IsRunning || pause.IsVisible);
                Assert.False(shell.IsSessionFeatureLockActive);
                Assert.Equal(Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
                Assert.Equal(Loc.Get("btn_start_session"), Assert.IsType<TextBlock>(presets.BtnStartSession.Content).Text);

                Dispatcher.UIThread.RunJobs();
                var recap = Assert.Single(shell.OwnedWindows.OfType<SessionCompleteWindow>());
                Assert.Equal("Wire Test", recap.FindControl<TextBlock>("TxtSessionName")!.Text);
                Assert.Equal(Loc.Get("label_session_ended_early"), recap.FindControl<TextBlock>("TxtMainMessage")!.Text);
                recap.Close();

                // Reaching the end: the recap says Completed.
                shell.StartSession(session);
                runner.Stop(completed: true);
                Dispatcher.UIThread.RunJobs();
                recap = Assert.Single(shell.OwnedWindows.OfType<SessionCompleteWindow>());
                Assert.Equal(Loc.Get("label_good_girl_3"), recap.FindControl<TextBlock>("TxtMainMessage")!.Text);
                recap.Close();
            }
            finally
            {
                runner.Stop();
                CoreEngine.StoppedHook = null;
                CoreSession.IsSessionRunningProvider = null;
                AvApp.Sessions = null;
                CoreEngine.Stop();
                shell.Close();
            }
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OldOrPreviousRunSuppressionDoesNotHideRecap(bool stale)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previous = AvApp.Sessions;
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            runner.SessionLog.LogReady += shell.OnSessionLogReady;
            try
            {
                shell.Show();
                shell._suppressSessionSummaryUntil = DateTime.UtcNow.AddSeconds(20);
                shell.StartSession(new Session { Id = "suppression", Name = "Suppression", DurationMinutes = 60 });
                if (stale) shell._suppressSessionSummaryUntil = DateTime.UtcNow.AddSeconds(-1);
                runner.Stop(completed: true);
                Dispatcher.UIThread.RunJobs();
                var recap = Assert.Single(shell.OwnedWindows.OfType<SessionCompleteWindow>());
                recap.Close();
            }
            finally
            {
                runner.Stop();
                CoreEngine.Stop();
                AvApp.Sessions = previous;
                shell.Close();
            }
            return Task.CompletedTask;
        });
    }

    private sealed class NoVideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, System.Action then) { }
    }

    /// <summary>WPF MainWindow.Presets.cs:1756-1793: short video finishes before a modal recap;
    /// playback surviving the 3s wait gets a passive recap.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecapWaitsForVideoThenRechecksPlayback(bool survivesTimeout)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            runner.SessionLog.LogReady += shell.OnSessionLogReady;
            var previousVideo = CoreEngine.Video;
            var video = new MandatoryVideoScheduler(new NoVideoHost(), library: () => new[] { "a.mp4" });
            try
            {
                var session = new Session { Id = "wire_video", Name = "Wire Video", Icon = "🧪", DurationMinutes = 1 };
                shell.StartSession(session);

                CoreEngine.Video = video;
                if (survivesTimeout) shell.Hide();
                runner.Stop(completed: true);
                Assert.True(video.Trigger(path: "a.mp4"));   // a new clip, before the recap posts
                int retries = 0;
                shell.SessionSummaryDelay = interval =>
                {
                    Assert.Equal(System.TimeSpan.FromMilliseconds(200), interval);
                    Assert.Empty(shell.OwnedWindows.OfType<SessionCompleteWindow>());
                    retries++;
                    if (!survivesTimeout) video.ForceCleanup();
                    return Task.CompletedTask;
                };
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(survivesTimeout ? 15 : 1, retries);
                var recap = survivesTimeout ? Assert.IsType<SessionCompleteWindow>(shell._liveSessionRecap)
                    : Assert.Single(shell.OwnedWindows.OfType<SessionCompleteWindow>());
                Assert.False(recap.Topmost);
                Assert.Equal(!survivesTimeout, recap.ShowActivated);
                // Avalonia modal ownership disables the backend, not Window.IsEnabled.
                Assert.Equal(!survivesTimeout, recap.IsDialog);
                if (survivesTimeout)
                {
                    shell.Close();
                    Assert.False(recap.IsVisible);
                }
                else recap.Close();
            }
            finally
            {
                runner.Stop();
                video.ForceCleanup();
                CoreEngine.Video = previousVideo;
                CoreSession.IsSessionRunningProvider = null;
                AvApp.Sessions = null;
                CoreEngine.Stop();
                shell.Close();
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF MainWindow.Presets.cs:1745,1777 rejects a recap during shutdown.
    /// Both the event entry and its queued callback must respect the shell lifetime.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ShutdownNeverOpensARecap(bool queuedBeforeShutdown, bool exitRequested)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            var opened = new System.Collections.Generic.List<SessionCompleteWindow>();
            using var hook = Window.WindowOpenedEvent.AddClassHandler<SessionCompleteWindow>((w, _) => opened.Add(w));
            var log = new SessionLogReadyEventArgs(new SessionLog { SessionName = "Lifecycle Test" });
            try
            {
                // Positive control: the event and window observer really work on this head.
                shell.OnSessionLogReady(null, log);
                Dispatcher.UIThread.RunJobs();
                Assert.Single(opened).Close();
                opened.Clear();

                if (queuedBeforeShutdown) shell.OnSessionLogReady(null, log);
                if (exitRequested)
                    shell.MarkExitRequestedForTest();
                else shell.Close();
                if (!queuedBeforeShutdown) shell.OnSessionLogReady(null, log);
                Dispatcher.UIThread.RunJobs();
                // Observe all recaps, including an orphan shown without the closed owner.
                Assert.Empty(opened);
            }
            finally
            {
                foreach (var recap in opened) recap.Close();
                shell.Close();
            }
            return Task.CompletedTask;
        });
    }

}
