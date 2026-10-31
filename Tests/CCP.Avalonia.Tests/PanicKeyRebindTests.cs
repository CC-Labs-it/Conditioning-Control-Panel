using System;
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
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class PanicKeyRebindTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ReenableDuringRunningSessionBinds(bool lockdown) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        s.PanicKeyEnabled = true;
        s.PanicKey = "F8";
        s.LockdownDisablePanicKey = true;
        s.LockdownForceStrictLock = false;
        var ld = LockdownService.Current = new LockdownService();
        var shell = new MainShellWindow();
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        int binds = 0;
        Action? press = null;
        PortalPanicShortcut.BindForTests = (down, up) =>
        {
            binds++;
            press = down;
            PortalPanicShortcut.SetSessionForTests("/test/rebind", up);
            return Task.FromResult<string?>("F8");
        };
        shell.Show();
        try
        {
            // Start/Resume while panic is disabled, just as in S20: no existing binding.
            if (lockdown) ld.Activate(TimeSpan.FromMinutes(30));
            else s.PanicKeyEnabled = false;
            shell.StartSession(new Session { Id = "panic_rebind", Name = "Panic rebind", DurationMinutes = 10 });
            runner.Pause();
            Assert.True(runner.IsPaused);
            shell.Named<Button>("BtnPauseSession")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(runner.IsPaused);
            Assert.True(runner.IsRunning);
            Assert.True(CoreEngine.IsRunning);
            Assert.False(PortalPanicShortcut.IsBound);
            Assert.Equal(0, binds);

            if (lockdown) ld.Deactivate();
            else s.PanicKeyEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(PortalPanicShortcut.IsBound);
            Assert.Equal(1, binds);

            // The registered callback has the existing portal panic handler; its stop path still works.
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            Assert.NotNull(press);
            shell.OnPortalPanicPressed();
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreEngine.IsRunning);
        }
        finally
        {
            ld.Deactivate();
            runner.Stop(completed: false);
            CoreEngine.StoppedHook = null;
            CoreEngine.Stop();
            s.PanicKeyEnabled = false;
            Dispatcher.UIThread.RunJobs();
            shell.Close();
            PortalPanicShortcut.BindForTests = null;
            PortalPanicShortcut.UnbindAsync().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DisableReleasesDuringRunningSession() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        s.PanicKeyEnabled = true;
        var shell = new MainShellWindow();
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        shell.Show();
        try
        {
            shell.StartSession(new Session { Id = "panic_release", Name = "Panic release", DurationMinutes = 10 });
            PortalPanicShortcut.SetSessionForTests("/test/release", null);
            Assert.True(PortalPanicShortcut.IsBound);
            s.PanicKeyEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreEngine.IsRunning);
            Assert.False(PortalPanicShortcut.IsBound);
        }
        finally
        {
            runner.Stop(completed: false);
            CoreEngine.Stop();
            shell.Close();
            PortalPanicShortcut.UnbindAsync().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
