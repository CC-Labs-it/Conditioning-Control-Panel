using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The blank Home browser on Windows (scout 2026-11-28 R1 to R3). WPF BrowserService never gates Home,
/// passes two WebView2 arguments and never hides a created WebView2.</summary>
public sealed class HomeBrowserWindowsTests
{
    private static readonly Uri Remote = new("https://bambicloud.com/");

    // Tests run in a CCP_USERDATA_DIR sandbox that refuses every real site before the gate is asked.
    private static void Unsandboxed(Action body)
    {
        var active = typeof(SandboxNet).GetProperty(nameof(SandboxNet.Active))!;
        var old = (bool)active.GetValue(null)!;
        try { active.SetValue(null, false); body(); }
        finally { active.SetValue(null, old); }
    }

    [Fact]
    public Task R1_HomeLoadsRemotePagesWhenTheScrolllerGateFailed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Unsandboxed(() =>
        {
            var loaded = new List<Uri>();
            var home = new WebHost { GateScrolller = false, BlockAds = true, NavigationInvoker = u => { loaded.Add(u); return true; } };
            home.NewScrolllerEngine()(false); // no CoreWebView2 in time, or Attach/AttachAds failed
            Assert.True(home.Navigate(Remote));
            Assert.Equal(new[] { Remote }, loaded);
            var start = new WebViewNavigationStartingEventArgs { Request = Remote };
            home.OnNavigationStarted(null, start);
            Assert.False(start.Cancel);
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task R1_GatedHostShowsTheErrorPanelForARefusedPage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Unsandboxed(() =>
        {
            var host = new WebHost { NativeViewFactory = () => new NativeWebView() };
            var panel = host.FindControl<Border>("Fallback")!;
            Assert.True(host.RecreateNativeView());
            host.NewScrolllerEngine()(false);
            Assert.False(panel.IsVisible);
            var start = new WebViewNavigationStartingEventArgs { Request = Remote };
            host.OnNavigationStarted(null, start);
            Assert.True(start.Cancel); // the licence rule stays
            Assert.True(panel.IsVisible);
            Assert.False(host.NativeView!.IsVisible); // the native view would draw over the panel
            Assert.Equal(ConditioningControlPanel.Avalonia.Controls.IconText.BareLoc("label_error_2"),
                host.FindControl<TextBlock>("TxtReason")!.Text);
            var local = new WebViewNavigationStartingEventArgs { Request = new Uri("http://127.0.0.1:5555/x.html") };
            host.OnNavigationStarted(null, local);
            Assert.False(local.Cancel);
            Assert.False(panel.IsVisible); // a page that loads clears the error
            Assert.True(host.NativeView!.IsVisible);
            var link = new WebViewNavigationStartingEventArgs { Request = Remote };
            host.OnNavigationStarted(null, link);
            Assert.True(link.Cancel);
            Assert.False(panel.IsVisible); // a refused link must not hide the page that is already running
            Assert.True(host.NativeView!.IsVisible);
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task R2_ShowingTheHomeHostRepairsTheWebView2Controller() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var offline = CoreSettings.Current.OfflineMode;
        Window? window = null;
        try
        {
            CoreSettings.Current.OfflineMode = false;
            var page = new SettingsTabView();
            var host = page.FindControl<WebHost>("BrowserWebHost")!;
            var repairs = 0;
            host.WindowsRenderControllerForTests = () => { repairs++; return IntPtr.Zero; };
            host.ScriptInvoker = _ => Task.FromResult<string?>(null); // an engine without a desktop
            page.SyncBrowserPrompt();
            Dispatcher.UIThread.RunJobs();
            Assert.False(host.IsVisible); // the load prompt covers it
            Assert.Equal(0, repairs);
            host.Source = new Uri("http://127.0.0.1:5555/home.html"); // the prompt click
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsVisible);
            Assert.Equal(1, repairs);

            window = new Window { Content = page };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, repairs);
            window.Content = null; // tab switch
            Dispatcher.UIThread.RunJobs();
            window.Content = page;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, repairs);
            window.Content = null;
            host.Source = null;
        }
        finally
        {
            window?.Close();
            CoreSettings.Current.OfflineMode = offline;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task R3_HomeProfileGetsWpfArgumentsInItsOwnFolder() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var host = new SettingsTabView().FindControl<WebHost>("BrowserWebHost")!;
        var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
        WebViewProfile.Apply(args); // CreateNativeView order: shared profile first, then the host's own options
        host.ApplyWindowsOptions(args);
        Assert.Equal("--autoplay-policy=no-user-gesture-required --disable-direct-composition-video-overlays", args.AdditionalBrowserArguments);
        Assert.Equal(System.IO.Path.Combine(CorePaths.UserData, "browser_data_avalonia_home"), args.UserDataFolder);
        return Task.CompletedTask;
    });
}
