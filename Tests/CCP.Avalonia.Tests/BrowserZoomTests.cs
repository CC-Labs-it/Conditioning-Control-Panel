using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserZoomTests
{
    private sealed class GtkHandle : IGtkWebViewPlatformHandle
    {
        public IntPtr WebKitWebView => new(17);
        public IntPtr Handle => WebKitWebView;
        public string HandleDescriptor => "gtk";
    }
    private sealed class WpeHandle : ILinuxWpePlatformHandle
    {
        public IntPtr WebKitWebView => new(19);
        public IntPtr WpeViewBackend => new(20);
        public IntPtr Handle => WebKitWebView;
        public string HandleDescriptor => "wpe";
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void LinuxHandlesSelectTheirOwnWebKitView(bool wpe)
    {
        IPlatformHandle handle = wpe ? new WpeHandle() : new GtkHandle();
        Assert.Equal((new IntPtr(wpe ? 19 : 17), wpe), WebViewZoom.LinuxView(handle));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void WindowsControllerZoomUsesGetAndSetSlots(bool write)
    {
        double value = 0.8;
        WebViewZoom.WinGetFn get = (IntPtr _, out double zoom) => { zoom = value; return 0; };
        WebViewZoom.WinSetFn set = (IntPtr _, double zoom) => { value = zoom; return 0; };
        var table = Marshal.AllocHGlobal(9 * IntPtr.Size);
        var controller = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(table, 7 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(get));
            Marshal.WriteIntPtr(table, 8 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(set));
            Marshal.WriteIntPtr(controller, table);
            if (write) { WebViewZoom.SetWindows(controller, 0.5); Assert.Equal(0.5, value); }
            else Assert.Equal(0.8, WebViewZoom.ReadWindows(controller));
        }
        finally { Marshal.FreeHGlobal(controller); Marshal.FreeHGlobal(table); GC.KeepAlive(get); GC.KeepAlive(set); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public Task StopWhileNativeReadIsPendingDoesNotEnterFullscreen(bool stop) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var pending = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var scripts = new List<string>();
            browser.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            browser.ZoomReader = () => pending.Task;
            browser.OnWebMessage("ccp_fullscreen_on");
            Assert.Null(shell.BrowserFullscreenWindow);
            if (stop) shell.StopBrowserFullscreen(); else shell.ExitBrowserFullscreen();
            var cleanup = Assert.Single(scripts);
            Assert.Contains("document.fullscreenElement && document.exitFullscreen", cleanup);
            Assert.Contains("document.webkitFullscreenElement && document.webkitExitFullscreen", cleanup);
            pending.SetResult(0.8);
            await Task.Delay(50);
            Assert.Null(shell.BrowserFullscreenWindow);
        }
        finally { shell.StopBrowserFullscreen(); shell.Close(); CoreSettings.ServiceProvider = old; }
    });

    private static Task WithBrowser(Action<MainShellWindow, WebHost, List<double>> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        settings.Current.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var calls = new List<double>();
            browser.ZoomInvoker = calls.Add;
            check(shell, browser, calls);
        }
        finally { shell.ExitBrowserFullscreen(); shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact] public Task SiteClickAppliesHalf() => WithBrowser((shell, browser, calls) =>
    {
        shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<RadioButton>("RbHypnoTube")!.IsChecked = true;
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        Assert.Equal("https://hypnotube.com/", browser.Source!.AbsoluteUri);
        Assert.Equal(0.75, Assert.Single(calls));
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        Assert.Equal(new[] { 0.75, 0.5 }, calls);
    });

    [Fact] public Task FirstReloadStartsAtDefaultZoom() => WithBrowser((shell, browser, calls) =>
    {
        shell.BtnReloadBrowser_Click(null, new RoutedEventArgs());
        Assert.Equal(0.75, Assert.Single(calls));
        shell.BtnReloadBrowser_Click(null, new RoutedEventArgs());
        Assert.Equal(new[] { 0.75, 0.5 }, calls);
    });

    [Fact] public Task FirstPopoutStartsAtDefaultZoom() => WithBrowser((shell, browser, calls) =>
    {
        shell.PopOutBrowser();
        Assert.Equal(0.75, Assert.Single(calls));
        Assert.NotNull(browser.Source);
    });

    [Fact] public Task LinkAppliesHalf() => WithBrowser((shell, browser, calls) =>
    {
        Assert.True(shell.NavigateToUrlInBrowser("https://example.com/"));
        Assert.Equal(0.5, Assert.Single(calls));
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        Assert.Equal(new[] { 0.5, 0.5 }, calls);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public Task FullscreenRestoresZoom(bool popout) => WithBrowser((shell, browser, calls) =>
    {
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        if (popout) shell.PopOutBrowser();
        calls.Clear();
        browser.OnWebMessage("ccp_fullscreen_on");
        Assert.Equal(1.0, calls[^1]);
        browser.OnWebMessage("ccp_fullscreen_off");
        Assert.Equal(0.5, calls[^1]);
    });

    [Fact] public Task NavigationDuringFullscreenResetsAndRestoresPrevious() => WithBrowser((shell, browser, calls) =>
    {
        browser.SetZoom(0.5);
        browser.ZoomReader = () => Task.FromResult<double?>(0.8);
        browser.OnWebMessage("ccp_fullscreen_on");
        Assert.Equal(new[] { 0.5, 1.0 }, calls);
        shell.NavigateToUrlInBrowser("https://example.com/");
        Assert.Equal(0.5, browser.Zoom);
        browser.OnWebMessage("ccp_fullscreen_off");
        Assert.Equal(0.8, calls[^1]);
    });

    [Fact] public Task NativeZoomIsReadAndRestored() => WithBrowser((shell, browser, calls) =>
    {
        browser.SetZoom(0.5);
        browser.ZoomReader = () => Task.FromResult<double?>(0.73);
        browser.OnWebMessage("ccp_fullscreen_on");
        Assert.Equal(1.0, calls[^1]);
        browser.OnWebMessage("ccp_fullscreen_off");
        Assert.Equal(0.73, calls[^1]);
    });

    [Fact] public Task RebuiltEngineReappliesZoom() => WithBrowser((shell, browser, calls) =>
    {
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        Assert.Equal(0.75, Assert.Single(calls));
        calls.Clear();
        browser.ZoomEngineCreated(null);
        Assert.Equal(0.75, Assert.Single(calls));
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        calls.Clear();
        browser.ZoomEngineCreated(null);
        Assert.Equal(0.5, Assert.Single(calls));
    });
}
