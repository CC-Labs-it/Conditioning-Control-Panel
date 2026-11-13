using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>One physical press heard by XInput2 and the portal is one panic press: counted twice it
/// took the double-press exit rung, closing the app on a single Escape (seen on Hyprland).</summary>
public sealed class PanicRouteTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void One_press_heard_on_both_routes_counts_once(bool x11First) => Presses(x11First, double_: false);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Two_presses_heard_on_both_routes_count_twice(bool x11First) => Presses(x11First, double_: true);

    [Fact]
    public void Letting_the_portal_go_ends_its_echo_window() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.PanicRoutes.Admit(PanicRouteGate.Route.Portal, 1000));
            MainShellWindow.ReleasePortalWhenUnused(shell);
            Assert.True(shell.PanicRoutes.Admit(PanicRouteGate.Route.X11, 1100));
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    /// <summary>P1 (r72): one panic source per race session; a shell report reaching us first never swallows the next X11 press.</summary>
    [Theory, InlineData(true), InlineData(false)]
    public void Race_shell_reports_and_x11_presses_have_one_source(bool listening) => AvaloniaTestDispatcher.Run(() =>
    {
        if (!System.OperatingSystem.IsLinux()) return;
        if (Application.Current is null) AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (oldProvider, oldTeardown, oldShell, settings) = (CoreSettings.ServiceProvider, PanicWatchdog.Teardown, LinuxRaceHost.Shell, new SettingsService());
        (CoreSettings.ServiceProvider, PanicWatchdog.Teardown, LinuxRaceHost.Shell) = (() => settings, () => { }, () => null);
        (CoreSettings.Current.PanicKeyEnabled, CoreSettings.Current.PanicKey, CoreSettings.Current.MasterVolume) = (true, "Escape", 0);
        using var p = LinuxRaceHostTests.StartCat();
        long clock = 1000; var shellPanics = 0; var shell = new MainShellWindow { PanicRouteClock = () => clock };
        var host = new LinuxRaceHost(new GameWindow(), p, _ => shellPanics++, () => listening, launchBackend: "x11");
        try
        {
            void Report(int seq) { host.ReadFrame(new JObject { ["type"] = "shell-escape", ["sequence"] = seq }); Dispatcher.UIThread.RunJobs(); }
            void X11Press() { if (listening) shell.OnX11PanicPress(); Dispatcher.UIThread.RunJobs(); clock += 100; }
            Report(1); X11Press(); Report(1); Report(2); X11Press();   // each press's report reaches us before its X11 event
            Assert.Equal((listening ? 0 : 2, listening ? 2 : 0), (shellPanics, shell.PanicPressCountForTest));   // the double press still exits
        }
        finally
        {
            host.Dispose(); shell.LeashHoldReleased(); shell.Close(); Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, PanicWatchdog.Teardown, LinuxRaceHost.Shell) = (oldProvider, oldTeardown, oldShell); PanicAftermath.EndQuiet();
        }
    });

    private static void Presses(bool x11First, bool double_) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.PanicKeyEnabled = true;
        CoreSettings.Current.MasterVolume = 0;
        var oldTeardown = PanicWatchdog.Teardown;
        PanicWatchdog.Teardown = () => { };           // a slow first press must not run the real teardown
        MainShellWindow? shell = null;
        try
        {
            long clock = 1000;
            shell = new MainShellWindow { PanicRouteClock = () => clock };
            Dispatcher.UIThread.RunJobs();            // the ctor's own work must not run inside the press
            if (x11First) { shell.OnX11PanicPress(); shell.OnPortalPanicActivated(); }
            else { shell.OnPortalPanicActivated(); shell.OnX11PanicPress(); }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, shell.PanicPressCountForTest);
            if (!double_) return;
            clock += 300;
            if (x11First) { shell.OnX11PanicPress(); shell.OnPortalPanicActivated(); }
            else { shell.OnPortalPanicActivated(); shell.OnX11PanicPress(); }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, shell.PanicPressCountForTest);   // a real double press still exits
        }
        finally
        {
            shell?.LeashHoldReleased();               // a never-shown window raises no Closed, so stop its clock here
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            PanicWatchdog.Teardown = oldTeardown;
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = oldProvider;
        }
    });
}
