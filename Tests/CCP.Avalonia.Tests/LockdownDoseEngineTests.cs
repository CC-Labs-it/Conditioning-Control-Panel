using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LockdownDoseEngineTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task ActivationStartsAnEnabledDoseAndGivesTheEngineBack()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = true;
            s.FlashEnabled = true;
            var count = s.TotalSessions;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                Assert.Contains("starts or restarts the engine", LockdownTabView.LockdownWarning(20, s, false));
                ld.Activate(TimeSpan.FromMinutes(20));
                Assert.False(CoreEngine.IsRunning);
                await Task.Delay(TimeSpan.FromSeconds(3)); // the real 2 second kickoff
                Assert.True(CoreEngine.IsRunning);
                Assert.Equal(count, s.TotalSessions); // system starts are not user sessions
                ld.Deactivate();
                Assert.False(CoreEngine.IsRunning);
            }
            finally { ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
        });
    }

    [Fact]
    public async Task RestartWaitsFourTicksAndRespectsSessionAndPanicHolds()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = true;
            s.FlashEnabled = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(20));
                CoreSession.IsSessionRunningProvider = () => true;
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                CoreSession.IsSessionRunningProvider = () => false;
                PanicAftermath.ArmQuiet();
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                PanicAftermath.ReleaseEngineHold();
                for (var i = 0; i < 3; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                shell.EnforceLockdownDoseEngine(false);
                Assert.True(CoreEngine.IsRunning);
                CoreSession.IsSessionRunningProvider = () => true;
                ld.Deactivate();
                Assert.True(CoreEngine.IsRunning); // the session now owns it
            }
            finally
            {
                CoreSession.IsSessionRunningProvider = null; PanicAftermath.EndQuiet();
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AUserStartWhileTheKeeperWaitsForThePortalIsNotTakenBack()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = s.PanicKeyEnabled = s.FlashEnabled = true;
            s.LockdownDisablePanicKey = false;
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var allowed = typeof(MainShellWindow).GetField("_portalAllowed", flags)!;
            var failed = typeof(MainShellWindow).GetField("_portalFailed", flags)!;
            var bind = typeof(MainShellWindow).GetField("_portalBind", flags)!;
            var oldAllowed = allowed.GetValue(null); var oldFailed = failed.GetValue(null); var oldBind = bind.GetValue(null);
            var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var pending = new TaskCompletionSource();
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "test");
                allowed.SetValue(null, true); failed.SetValue(null, false); bind.SetValue(null, pending.Task);
                ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true);
                Assert.False(CoreEngine.IsRunning);
                PortalPanicShortcut.SetSessionForTests("/test/session", null);
                shell.StartEngineFromUser();
                Assert.True(CoreEngine.IsRunning);
                ld.Deactivate();
                Assert.True(CoreEngine.IsRunning);
                pending.SetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.True(CoreEngine.IsRunning);
            }
            finally
            {
                PortalPanicShortcut.SetSessionForTests(null, null);
                allowed.SetValue(null, oldAllowed); failed.SetValue(null, oldFailed); bind.SetValue(null, oldBind);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EmptyDoseAndDisabledKeeperStandDownAndPreexistingEngineIsLeftRunning()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.FlashEnabled = s.SubliminalEnabled = s.SpiralEnabled = s.PinkFilterEnabled = false;
            s.BouncingTextEnabled = s.BubblesEnabled = s.MandatoryVideosEnabled = s.MindWipeEnabled = false;
            s.LockCardEnabled = s.BubbleCountEnabled = s.BrainDrainEnabled = s.AudioOnlySession = s.PopQuizEnabled = false;
            s.CornerGifOverlays.Clear();
            s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true);
                Assert.False(CoreEngine.IsRunning); // no feature selection on this head yet
                ld.Deactivate();
                s.FlashEnabled = true;
                s.LockdownDoseKeeperEnabled = false;
                Assert.DoesNotContain("starts or restarts the engine", LockdownTabView.LockdownWarning(20, s, false));
                ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true);
                Assert.False(CoreEngine.IsRunning);
                ld.Deactivate();
                s.LockdownDoseKeeperEnabled = true;
                shell.StartEngine();
                Assert.True(CoreEngine.IsRunning);
                ld.Activate(TimeSpan.FromMinutes(20));
                MainShellWindow.StopEngine();
                shell.EnforceLockdownDoseEngine(true); // keeper restarts an engine that predated lockdown
                Assert.True(CoreEngine.IsRunning);
                ld.Deactivate();
                Assert.True(CoreEngine.IsRunning);
            }
            finally { ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
            return Task.CompletedTask;
        });
    }
}
