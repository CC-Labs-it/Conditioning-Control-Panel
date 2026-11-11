using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserFullscreenTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FullscreenReturnsBrowserAndStopRejectsLatePageEntry(bool popout) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            var container = page.FindControl<Grid>("BrowserContainer")!;
            if (popout) shell.PopOutBrowser();
            browser.OnWebMessage("ccp_fullscreen_on");
            var host = Assert.IsType<Window>(shell.BrowserFullscreenWindow);
            Assert.Same(browser, host.Content);
            browser.OnWebMessage("ccp_exit_fullscreen");
            Assert.Null(shell.BrowserFullscreenWindow);
            if (popout) Assert.Same(browser, shell.BrowserPopoutWindow!.Content);
            else Assert.Contains(browser, container.Children);
            browser.OnWebMessage("ccp_fullscreen_on");
            shell.StopBrowserFullscreen();
            Assert.Null(shell.BrowserFullscreenWindow);
            browser.OnWebMessage("ccp_fullscreen_on");
            Assert.Null(shell.BrowserFullscreenWindow);
            browser.OnNavigationCompleted(new Uri("https://example.com/"));
            browser.OnWebMessage("ccp_fullscreen_on");
            Assert.NotNull(shell.BrowserFullscreenWindow);
            browser.OnWebMessage("ccp_key:F11:0:0");
            Assert.Null(shell.BrowserFullscreenWindow);
        }
        finally { shell.ExitBrowserFullscreen(); shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.F11)]
    public Task WindowKeysAndCloseReturnTheBrowser(Key key) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            var container = page.FindControl<Grid>("BrowserContainer")!;
            browser.OnWebMessage("ccp_fullscreen_on");
            var host = Assert.IsType<Window>(shell.BrowserFullscreenWindow);
            host.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Assert.Null(shell.BrowserFullscreenWindow);
            Assert.Contains(browser, container.Children);
            browser.OnWebMessage("ccp_fullscreen_on");
            host = Assert.IsType<Window>(shell.BrowserFullscreenWindow);
            host.Close();
            Assert.Null(shell.BrowserFullscreenWindow);
            Assert.Contains(browser, container.Children);
        }
        finally { shell.ExitBrowserFullscreen(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    private sealed class Topology : IDisplayTopology
    {
        public int ActiveScreenCount => 2;
        public int CloneResult, ExtendResult;
        public Action<bool>? OnApply;
        public List<bool> Calls = new();
        public int Apply(bool clone) { Calls.Add(clone); OnApply?.Invoke(clone); return clone ? CloneResult : ExtendResult; }
    }
    private static Screen ScreenAt(int x)
    {
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen), new object[] { new PlatformHandle(new IntPtr(x + 1), "test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, new PixelRect(x, 0, 1920, 1080));
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, new PixelRect(x, 0, 1920, 1080));
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1d);
        return screen;
    }
    [Theory]
    [InlineData(true, -1, 2, true)]
    [InlineData(false, -1, 2, false)]
    [InlineData(true, 1, 2, false)]
    [InlineData(false, -2, 2, true)]
    [InlineData(true, 9, 2, false)]
    [InlineData(true, -1, 1, false)]
    public Task CloneOnlyForMultipleSelectedScreens(bool dual, int target, int count, bool clone) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var oldSettings = CoreSettings.ServiceProvider; var oldScreens = ScreenList.QueryOverride; var oldMirror = ScreenMirrorHead.Override;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        settings.Current.DualMonitorEnabled = dual; settings.Current.GlobalTargetMonitor = target;
        ScreenList.QueryOverride = _ => count == 2 ? new[] { ScreenAt(0), ScreenAt(1920) } : new[] { ScreenAt(0) };
        var topology = new Topology(); var mirror = new DisplayMirror(topology); ScreenMirrorHead.Override = mirror;
        var shell = new MainShellWindow();
        try {
            shell.Show(); var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            browser.OnWebMessage("ccp_fullscreen_on"); Assert.NotNull(shell.BrowserFullscreenWindow);
            Assert.Equal(clone, mirror.IsMirroring);
            browser.OnWebMessage("ccp_fullscreen_off");
            Assert.False(mirror.IsMirroring); Assert.Equal(clone ? new[] { true, false } : Array.Empty<bool>(), topology.Calls);
        } finally { shell.ExitBrowserFullscreen(); shell.Close(); ScreenList.QueryOverride = oldScreens; ScreenMirrorHead.Override = oldMirror; CoreSettings.ServiceProvider = oldSettings; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task FailedCloneAndExtendRemainSafeAndRetryable() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var oldSettings = CoreSettings.ServiceProvider; var oldScreens = ScreenList.QueryOverride; var oldMirror = ScreenMirrorHead.Override;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings; settings.Current.DualMonitorEnabled = true; settings.Current.GlobalTargetMonitor = -1;
        ScreenList.QueryOverride = _ => new[] { ScreenAt(0), ScreenAt(1920) };
        var topology = new Topology { CloneResult = 31 }; var mirror = new DisplayMirror(topology); ScreenMirrorHead.Override = mirror;
        var shell = new MainShellWindow();
        try {
            shell.Show(); var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            browser.OnWebMessage("ccp_fullscreen_on"); Assert.NotNull(shell.BrowserFullscreenWindow); browser.OnWebMessage("ccp_fullscreen_off");
            Assert.Equal(new[] { true }, topology.Calls); Assert.False(mirror.IsMirroring);
            topology.CloneResult = 0; browser.OnWebMessage("ccp_fullscreen_on"); Assert.True(mirror.IsMirroring);
            topology.ExtendResult = 31; browser.OnWebMessage("ccp_fullscreen_off"); Assert.True(mirror.IsMirroring); Assert.Null(shell.BrowserFullscreenWindow);
            topology.ExtendResult = 0; shell.StopBrowserFullscreen(); Assert.False(mirror.IsMirroring);
            Assert.Equal(new[] { true, true, false, false }, topology.Calls);
        } finally { topology.ExtendResult = 0; shell.ExitBrowserFullscreen(); shell.Close(); ScreenList.QueryOverride = oldScreens; ScreenMirrorHead.Override = oldMirror; CoreSettings.ServiceProvider = oldSettings; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task StopRejectsTransitionEventButAllowsEntryWithoutNavigation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup(); var old = CoreSettings.ServiceProvider; var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try {
            shell.Show(); var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            browser.OnWebMessage("ccp_fullscreen_on"); Assert.NotNull(shell.BrowserFullscreenWindow);
            shell.StopBrowserFullscreen(); browser.OnWebMessage("ccp_fullscreen_on"); Assert.Null(shell.BrowserFullscreenWindow);
            Dispatcher.UIThread.RunJobs(); browser.OnWebMessage("ccp_fullscreen_on"); Assert.NotNull(shell.BrowserFullscreenWindow);
        } finally { shell.ExitBrowserFullscreen(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StopDuringCloneCompletesTeardown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup(); var oldSettings = CoreSettings.ServiceProvider; var oldScreens = ScreenList.QueryOverride; var oldMirror = ScreenMirrorHead.Override;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings; settings.Current.DualMonitorEnabled = true; settings.Current.GlobalTargetMonitor = -1;
        ScreenList.QueryOverride = _ => new[] { ScreenAt(0), ScreenAt(1920) };
        var topology = new Topology(); var mirror = new DisplayMirror(topology); ScreenMirrorHead.Override = mirror; var shell = new MainShellWindow();
        try {
            shell.Show(); topology.OnApply = clone => { if (clone) shell.StopBrowserFullscreen(); };
            shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!.OnWebMessage("ccp_fullscreen_on");
            Assert.Null(shell.BrowserFullscreenWindow); Assert.False(mirror.IsMirroring); Assert.Equal(new[] { true, false }, topology.Calls);
        } finally { shell.ExitBrowserFullscreen(); shell.Close(); ScreenList.QueryOverride = oldScreens; ScreenMirrorHead.Override = oldMirror; CoreSettings.ServiceProvider = oldSettings; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VisibleRemoteCardBlocksLateFullscreenAfterStopAndNavigation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup(); var old = CoreSettings.ServiceProvider; var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try {
            shell.Show(); var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>(); browser.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); };
            var overlay = shell.FindControl<Border>("RemoteControlOverlay")!;
            browser.OnWebMessage("ccp_fullscreen_on"); Assert.NotNull(shell.BrowserFullscreenWindow);
            shell.StopBrowserFullscreen(); overlay.IsVisible = true; Dispatcher.UIThread.RunJobs();
            browser.OnWebMessage("ccp_fullscreen_on"); Assert.Null(shell.BrowserFullscreenWindow);
            Assert.Contains(scripts, script => script.Contains("document.exitFullscreen"));
            browser.OnNavigationCompleted(new Uri("https://example.com/"));
            browser.OnWebMessage("ccp_fullscreen_on"); Assert.Null(shell.BrowserFullscreenWindow);
            overlay.IsVisible = false; browser.OnWebMessage("ccp_fullscreen_on"); Assert.NotNull(shell.BrowserFullscreenWindow);
        } finally { shell.ExitBrowserFullscreen(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FullscreenReassertsEffectsOnEntryActivationAndTimerWithoutCoveringMandatoryVideo() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup(); var old = CoreSettings.ServiceProvider; var oldVideo = CoreEngine.Video;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings; CoreEngine.Video = null;
        var shell = new MainShellWindow();
        var tint = new ConditioningControlPanel.Avalonia.Views.Overlays.TintOverlayWindow();
        var spiral = new ConditioningControlPanel.Avalonia.Views.Overlays.SpiralOverlayWindow();
        var other = new Window(); var raised = new List<Window>();
        shell.BrowserEffectWindows = () => new[] { other, spiral, tint }; shell.BrowserRaiseEffect = raised.Add;
        try {
            shell.Show(); tint.Show(); spiral.Show(); other.Show();
            var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            browser.OnWebMessage("ccp_fullscreen_on"); var host = Assert.IsType<Window>(shell.BrowserFullscreenWindow);
            Assert.Equal(new Window[] { tint, spiral }, raised); raised.Clear();
            typeof(WindowBase).GetMethod("HandleActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(host, null);
            Assert.Equal(new Window[] { tint, spiral }, raised); raised.Clear();
            var timer = (DispatcherTimer)typeof(MainShellWindow).GetField("_browserEffectTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)!;
            Assert.True(timer.IsEnabled); Dispatcher.UIThread.RunJobs(); raised.Clear();
            var ticks = new List<Window[]>(); var twoTicks = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            timer.Tick += (_, _) => { ticks.Add(raised.ToArray()); raised.Clear(); if (ticks.Count >= 2) twoTicks.TrySetResult(true); };
            await Task.WhenAny(twoTicks.Task, Task.Delay(3000)); Assert.True(twoTicks.Task.IsCompleted);
            foreach (var tick in ticks) {
                Assert.True(tick.Length >= 2); Assert.Equal(0, tick.Length % 2);
                for (int i = 0; i < tick.Length; i += 2) { Assert.Same(tint, tick[i]); Assert.Same(spiral, tick[i + 1]); }
            }
            raised.Clear();
            CoreEngine.Video = new MandatoryVideoScheduler(null!);
            typeof(MandatoryVideoScheduler).GetField("_playing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(CoreEngine.Video, true);
            shell.TickBrowserFullscreenEffects(); Assert.Empty(raised);
            CoreEngine.Video = null; shell.ExitBrowserFullscreen(); Assert.False(timer.IsEnabled); raised.Clear();
            var tickCount = ticks.Count; await Task.Delay(700); Assert.Equal(tickCount, ticks.Count); Assert.Empty(raised);
        } finally { CoreEngine.Video = oldVideo; shell.ExitBrowserFullscreen(); shell.Close(); tint.Close(); spiral.Close(); other.Close(); CoreSettings.ServiceProvider = old; }
    });

}
