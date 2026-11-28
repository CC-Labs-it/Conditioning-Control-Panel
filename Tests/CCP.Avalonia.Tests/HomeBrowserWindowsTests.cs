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

    private sealed class LateCore : IWindowsWebView2PlatformHandle
    {
        public IntPtr CoreWebView2 { get; set; }
        public IntPtr CoreWebView2Controller => IntPtr.Zero;
        public IntPtr Handle => IntPtr.Zero;
        public string HandleDescriptor => "WebView2";
    }

    private static WebHost HeldHome(List<Uri> loaded) =>
        new() { GateScrolller = false, BlockAds = true, NavigationInvoker = u => { loaded.Add(u); return true; } };

    // Home's page and every navigation its engine starts wait until the ad filter is on: not loaded, refused at start.
    private static void AssertHeld(WebHost home, List<Uri> loaded)
    {
        Assert.Null(home.ScrolllerGate);
        Assert.True(home.Navigate(Remote)); // accepted, held for the report
        Assert.Empty(loaded);
        var start = new WebViewNavigationStartingEventArgs { Request = Remote };
        home.OnNavigationStarted(null, start);
        Assert.True(start.Cancel);
    }

    private static void AssertReleased(WebHost home, List<Uri> loaded)
    {
        Assert.True(home.Navigate(Remote));
        Assert.Equal(new[] { Remote }, loaded);
        var start = new WebViewNavigationStartingEventArgs { Request = Remote };
        home.OnNavigationStarted(null, start);
        Assert.False(start.Cancel);
    }

    [Fact]
    public Task R1_HomeStaysHeldThroughALateCoreUntilItsAdFilterAttaches() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Unsandboxed(() =>
        {
            var loaded = new List<Uri>();
            var home = HeldHome(loaded);
            var ready = home.NewScrolllerEngine();
            var core = new LateCore();
            var retries = new Queue<Action>();
            var attached = new List<IntPtr>();
            ScrolllerWebHostGate.AttachWindows(core, () => core, ready, ScrolllerWebHostGate.CoreTries(blockAds: true), blockAds: true,
                c => { attached.Add(c); return true; }, retries.Enqueue);
            // Past the point where a non-ad host gives up (CoreRetries): still polling, nothing reported.
            for (var i = 0; i <= ScrolllerWebHostGate.CoreRetries + 1; i++) retries.Dequeue()();
            Assert.Single(retries);
            Assert.Empty(attached);
            AssertHeld(home, loaded);
            core.CoreWebView2 = new IntPtr(42);
            retries.Dequeue()();
            Assert.Equal(new[] { new IntPtr(42) }, attached);
            Assert.Empty(retries);
            Assert.True(home.ScrolllerGate);
            AssertReleased(home, loaded);

            // No core ever: the ad host stops polling but never reports, so it never loads unfiltered.
            var dead = HeldHome(loaded = new List<Uri>());
            var none = new LateCore();
            ScrolllerWebHostGate.AttachWindows(none, () => none, dead.NewScrolllerEngine(), ScrolllerWebHostGate.CoreTries(blockAds: true), blockAds: true,
                _ => throw new InvalidOperationException("no core to attach"), retries.Enqueue);
            while (retries.TryDequeue(out var next)) next();
            AssertHeld(dead, loaded);
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task R1_HomeOnLinuxWaitsForItsFilterPastTheDeadline() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Unsandboxed(() =>
        {
            var loaded = new List<Uri>();
            var home = HeldHome(loaded);
            var expiries = new List<Action>();
            var deadline = ScrolllerWebHostGate.LinuxDeadline(home.NewScrolllerEngine(), blockAds: true, expiries.Add);
            Assert.Empty(expiries); // no 5 s release for the ad host
            AssertHeld(home, loaded);
            deadline.Report(false); // the filter's own failure: logged, the page loads (WPF BrowserService.cs:613-616)
            Assert.False(home.ScrolllerGate);
            AssertReleased(home, loaded);

            var gated = new WebHost();
            _ = ScrolllerWebHostGate.LinuxDeadline(gated.NewScrolllerEngine(), blockAds: false, expiries.Add);
            Assert.Single(expiries)();
            Assert.False(gated.ScrolllerGate); // other hosts keep the 5 s "no gate"
        });
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
