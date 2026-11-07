using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Program session toasts from the shell, WPF ProgramsTab.cs:1864-1940 and Presets.cs:2013,2058.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ProgramsToastsTests
{
    private static Task Run(Func<MainShellWindow, ProgramService, SessionRunner, Task> body, bool readOnly = false) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-programs-toast-").FullName;
        var (oldPrograms, oldSessions, oldTask) = (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider);
        var settings = CoreSettings.Current;
        var (seenTab, seenIntro, motion) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel);
        settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
        settings.MotionLevel = MotionLevel.Full;
        // Pinned: an idle engine's start warns about an empty local image library (WPF StartEngine), whatever ran before.
        var (flash, source, audioOnly, oldAssets) = (settings.FlashEnabled, settings.MediaSource, settings.AudioOnlySession, CorePaths.EffectiveAssetsProvider);
        (settings.FlashEnabled, settings.MediaSource, settings.AudioOnlySession) = (true, "local", false);
        CorePaths.EffectiveAssetsProvider = () => Path.Combine(dir, "assets");
        MainShellWindow? shell = null;
        try
        {
            CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
            var svc = AvApp.Programs = new ProgramService(Path.Combine(dir, "programs.json"), readOnly: false);
            Assert.NotNull(svc.Enroll(svc.GetProgram("first_week")!));
            if (readOnly)
            {
                svc.Dispose();
                svc = AvApp.Programs = new ProgramService(Path.Combine(dir, "programs.json"), readOnly: true);
            }
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            shell = new MainShellWindow();
            runner.SessionLog.LogReady += shell.OnSessionLogReady;
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await body(shell, svc, runner);
        }
        finally
        {
            CoreEngine.StoppedHook = null;
            if (shell != null)
            {
                AvApp.Sessions!.SessionLog.LogReady -= shell.OnSessionLogReady;
                foreach (var w in shell.OwnedWindows.ToList()) w.Close();
                shell.Close();
            }
            Dispatcher.UIThread.RunJobs();
            AvApp.Sessions?.Stop();
            AvApp.Programs?.Dispose();
            (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider) = (oldPrograms, oldSessions, oldTask);
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel) = (seenTab, seenIntro, motion);
            (settings.FlashEnabled, settings.MediaSource, settings.AudioOnlySession) = (flash, source, audioOnly);
            CorePaths.EffectiveAssetsProvider = oldAssets;
            CoreSession.IsSessionRunningProvider = null;
            Directory.Delete(dir, true);
        }
    });

    private static Grid Toast(Control toast) => (Grid)((Border)toast).Child!;
    private static string ToastText(Control toast) => Toast(toast).Children.OfType<TextBlock>().First().Text!;

    [Fact]
    public Task ProgramSessionAnnouncesStartEarlyEndAndCompletionWithViewToday() => Run(async (shell, svc, runner) =>
    {
        var host = new StackPanel();
        AvApp.Notifications.AttachHost(host);   // replays anything an earlier test queued, before we listen
        // Recorded as shown: a toast's own (real) expiry timer may remove it before the assert.
        var toasts = new System.Collections.Generic.List<Control>();
        host.Children.CollectionChanged += (_, e) => { foreach (Control c in e.NewItems ?? Array.Empty<Control>()) toasts.Add(c); };
        var title = svc.ActiveProgram!.Title;
        // WPF StartProgramSession -> StartEngineFromUser -> StartEngine (StartStop.cs:313-318): an idle engine's
        // start warns about the empty image library first, then OnSessionStarted announces the program.
        var noImages = string.Format(Loc.Get("msg_no_flash_images"), Loc.Get("nav_door_library"), Loc.Get("tab_assets"));

        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(runner.IsRunning && svc.IsProgramSession(runner.CurrentSession));
        Assert.Equal(new[] { noImages, Loc.GetF("programs_toast_session_started", title, svc.ActiveEnrollment!.CurrentDay) },
            toasts.Select(ToastText));

        // Panic cuts it short: WPF still says so (OnSessionStopped -> AnnounceProgramSessionEnded).
        PanicSurfaces.StopAll("test", shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, toasts.Count);
        Assert.Equal(Loc.GetF("programs_toast_session_ended_early", title), ToastText(toasts[^1]));

        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        runner.Stop(completed: true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[]
        {
            noImages,
            Loc.GetF("programs_toast_session_started", title, svc.ActiveEnrollment!.CurrentDay),
            Loc.GetF("programs_toast_session_ended_early", title),
            noImages,   // the panic stopped the engine, so this start warns again
            Loc.GetF("programs_toast_session_started", title, svc.ActiveEnrollment!.CurrentDay),
            Loc.GetF("programs_toast_session_completed", title),
        }, toasts.Select(ToastText));
        foreach (var w in shell.OwnedWindows.ToList()) w.Close();   // the session recap
        Dispatcher.UIThread.RunJobs();

        // "View today" routes back to Programs.
        shell.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();
        var view = Toast(toasts[^1]).Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(b => b.Name == "ToastAction");
        Assert.Equal(Loc.Get("programs_toast_view_today"), ((TextBlock)view.Content!).Text);
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("programs", shell.CurrentTab);

        // A foreign session is silent at both ends: only its idle engine's library warning shows.
        var count = toasts.Count;
        shell.StartSession(Session.MorningDrift);
        Dispatcher.UIThread.RunJobs();
        Assert.True(runner.IsRunning);
        runner.Stop(completed: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { noImages }, toasts.Skip(count).Select(ToastText));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LockedCardRoutesToAccountBeforeEnrollmentChecks(bool readOnly) => Run(async (shell, svc, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => false;
        shell.ShowTab("programs");
        Dispatcher.UIThread.RunJobs();
        var before = svc.ActiveEnrollment;
        var premium = svc.Library.First(p => p.Tier == ConditioningControlPanel.Models.Program.ProgramTier.Premium);
        await shell.EnrollProgramAsync(premium.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("appsettings", shell.CurrentTab);
        Assert.Equal("account", shell.AppSettingsPage!.CheckedSectionKey);
        Assert.Equal(readOnly, svc.IsReadOnly);
        Assert.Same(before, svc.ActiveEnrollment);
        Assert.DoesNotContain(shell.OwnedWindows, w => w is ConditioningControlPanel.Avalonia.Views.Dialogs.ProgramEnrollDialog);
    }, readOnly);

}
