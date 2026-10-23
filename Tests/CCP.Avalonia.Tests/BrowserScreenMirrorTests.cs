using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Browser.cs:2727-2763: page fullscreen clones the displays when the "Show content on" pick
/// covers more than one screen, and leaving fullscreen puts extend back.</summary>
public sealed class BrowserScreenMirrorTests
{
    private sealed class FakeTopology : IDisplayTopology
    {
        public int ActiveScreenCount => 2;
        public List<bool> Calls { get; } = new();
        public int Apply(bool clone) { Calls.Add(clone); return 0; }
    }

    private static Screen MakeScreen(int x, bool primary)
    {
        // Same Avalonia 12.1 internals as MultiMonitorResultTests.MakeScreen.
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(x + 1), "headless-test") })!;
        var bounds = new PixelRect(x, 0, 1920, 1080);
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, primary);
        return screen;
    }

    private static Task Run(bool dualMonitor, Action<MainShellWindow, WebHost, FakeTopology, DisplayMirror> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (offline, dual, target) = (s.OfflineMode, s.DualMonitorEnabled, s.GlobalTargetMonitor);
            var oldQuery = ScreenList.QueryOverride;
            s.OfflineMode = false;
            s.DualMonitorEnabled = dualMonitor;
            s.GlobalTargetMonitor = MonitorTarget.FollowGlobal;
            var screens = new[] { MakeScreen(0, true), MakeScreen(1920, false) };
            ScreenList.QueryOverride = _ => screens;
            var topo = new FakeTopology();
            var mirror = new DisplayMirror(topo);
            ScreenMirrorHead.Override = mirror;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                var page = shell.Named<SettingsTabView>("SettingsTab")!;
                check(shell, page.FindControl<WebHost>("BrowserWebHost")!, topo, mirror);
            }
            finally
            {
                shell.Close();
                ScreenMirrorHead.Override = null;
                ScreenList.QueryOverride = oldQuery;
                (s.OfflineMode, s.DualMonitorEnabled, s.GlobalTargetMonitor) = (offline, dual, target);
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task FullscreenAcrossBothScreensClonesAndEscapeRestoresExtend() => Run(true, (shell, host, topo, mirror) =>
    {
        host.Navigate(new Uri("http://127.0.0.1:9/video"));
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.True(shell.IsBrowserFullscreen);
        Assert.True(mirror.IsMirroring);
        Assert.Equal(new[] { true }, topo.Calls);

        shell.BrowserPopoutWindow!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(shell.IsBrowserFullscreen);
        Assert.False(mirror.IsMirroring);
        Assert.Equal(new[] { true, false }, topo.Calls);
    });

    [Fact]
    public Task ClosingTheFullscreenWindowFromOutsideRestoresExtend() => Run(true, (shell, host, topo, mirror) =>
    {
        host.Navigate(new Uri("http://127.0.0.1:9/video"));
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        shell.BrowserPopoutWindow!.Close();
        Assert.False(mirror.IsMirroring);
        Assert.Equal(new[] { true, false }, topo.Calls);
    });

    [Fact]
    public Task PopoutFullscreenOffRestoresExtendAndKeepsThePopout() => Run(true, (shell, host, topo, mirror) =>
    {
        // A pop-out goes fullscreen in place, so leaving it never closes the window: the exit itself must restore extend.
        shell.Named<SettingsTabView>("SettingsTab")!.FindControl<Button>("BtnPopOutBrowser")!
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var popout = shell.BrowserPopoutWindow;
        Assert.NotNull(popout);
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.True(mirror.IsMirroring);
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOffMessage);
        Assert.Same(popout, shell.BrowserPopoutWindow);
        Assert.False(mirror.IsMirroring);
        Assert.Equal(new[] { true, false }, topo.Calls);
    });

    [Fact]
    public Task ContentPinnedToOneScreenIsNeverCloned() => Run(false, (shell, host, topo, mirror) =>
    {
        host.Navigate(new Uri("http://127.0.0.1:9/video"));
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.True(shell.IsBrowserFullscreen);
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOffMessage);
        Assert.False(shell.IsBrowserFullscreen);
        Assert.Empty(topo.Calls);
    });
}
