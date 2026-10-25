using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Platform;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralEmbedViewTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task EmbedStateHonorsOsAnimationPreference() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProbe = ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe;
        var oldMotion = CoreSettings.Current.MotionLevel;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = () => false;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
            string? sent = null;
            using var view = new SpiralEmbedView("map") { SendJson = json => { sent = json; return Task.CompletedTask; } };
            view.Receive("{\"type\":\"spiral:ready\"}");
            view.PostState(null);
            Assert.True((bool)JObject.Parse(sent!)["reduced_motion"]!);
        }
        finally
        {
            CoreSettings.Current.MotionLevel = oldMotion;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = oldProbe;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TabUsesCurrentBlock_UpdatesWithoutRebuilding_AndWithdraws() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldDescent = ConditioningControlPanel.Avalonia.App.Descent;
        var oldFuse = ConditioningControlPanel.Avalonia.App.DescentCountdown;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldEngine = ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.EngineAvailable;
        var oldStart = ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.StartEmbed;
        var service = new DescentService();
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.DescentMigrationCompleted = true;
        settings.Current.MotionLevel = MotionLevel.Off;
        ConditioningControlPanel.Avalonia.App.Descent = service;
        ConditioningControlPanel.Avalonia.App.DescentCountdown = null;
        ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.EngineAvailable = () => true;
        var sent = new List<string>();
        ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.StartEmbed = e =>
        {
            e.SendJson = json => { sent.Add(json); return Task.CompletedTask; };
            e.Receive("{\"type\":\"spiral:ready\"}");
        };
        // Feed the service's accepted snapshot without network or credentials; events use the real publisher.
        void Block(int days)
        {
            typeof(DescentService).GetProperty(nameof(DescentService.Current))!.SetValue(service,
                new DescentBlock { DevotionDays = days });
            service.NotifySurfaces("test snapshot");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Block(3);
        var tab = new ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView();
        var window = new Window { Content = tab };
        try
        {
            window.Show();
            var host = tab.FindControl<Grid>("EmbedHost")!;
            var embed = Assert.IsType<SpiralEmbedView>(Assert.Single(host.Children));
            Assert.Equal(3, (int)JObject.Parse(Assert.Single(sent))["devotion_days"]!);
            Block(8);
            Assert.Same(embed, Assert.Single(host.Children));
            Assert.Equal(8, (int)JObject.Parse(sent[^1])["devotion_days"]!);
            service.Reset();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
            Assert.True(tab.FindControl<Border>("WaitingPanel")!.IsVisible);
            tab.IsVisible = false;
            Block(9);
            Assert.Empty(host.Children);
            tab.IsVisible = true;
            Assert.Single(host.Children);
            tab.StopForPanic();
            Assert.False(tab.IsVisible);
            Assert.Empty(host.Children);
            Block(10);
            Assert.Empty(host.Children); // panic cannot be undone by a late account callback
        }
        finally
        {
            window.Close();
            ConditioningControlPanel.Avalonia.App.Descent = oldDescent;
            ConditioningControlPanel.Avalonia.App.DescentCountdown = oldFuse;
            CoreSettings.ServiceProvider = oldProvider;
            ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.EngineAvailable = oldEngine;
            ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.StartEmbed = oldStart;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PageHandshakeSendsLatestState_ThenLiveUpdates_DisposeStopsMessages() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var sent = new List<string>();
        using var view = new SpiralEmbedView("map")
        { SendJson = json => { sent.Add(json); return Task.CompletedTask; } };
        int ready = 0;
        view.Ready += (_, _) => ready++;
        view.PostState(new DescentBlock { DevotionDays = 1 });
        view.PostState(new DescentBlock { DevotionDays = 8 });
        view.Receive("{\"type\":\"ready\"}");
        view.Receive("not json");
        Assert.Empty(sent);
        Assert.False(view.IsReady);
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.Equal(8, (int)JObject.Parse(Assert.Single(sent))["devotion_days"]!);
        Assert.True(view.IsReady);
        Assert.Equal(1, ready);
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.Equal(1, ready);
        view.PostState(new DescentBlock { DevotionDays = 9 });
        Assert.Equal(2, sent.Count);
        Assert.Equal(9, (int)JObject.Parse(sent[1])["devotion_days"]!);
        view.Dispose();
        view.PostState(new DescentBlock { DevotionDays = 10 });
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.False(view.IsReady);
        Assert.Equal(2, sent.Count);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SuccessfulNavigationRevealsBeforeReady_FailureDisposesInstead() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var view = new SpiralEmbedView("map");
        int navigated = 0, failed = 0;
        view.Navigated += (_, _) => navigated++;
        view.Failed += (_, _) => failed++;
        view.OnNavigated(new Uri("https://evil.test/"), true);
        Assert.Equal(0, navigated);
        view.OnNavigated(new Uri(view.Url), true);
        Assert.Equal(1, navigated);
        Assert.False(view.IsReady);
        view.OnNavigated(new Uri(view.Url), false);
        Assert.Equal(1, failed);
        Assert.True(view.HasFailed);
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.False(view.IsReady);
        view.OnNavigated(new Uri(view.Url), true);
        Assert.Equal(1, navigated);
        return Task.CompletedTask;
    });

    [Fact]
    public Task MissingEngineFailsOnce_AndDropsNativeHost() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var view = new SpiralEmbedView("map");
        int failed = 0;
        view.Failed += (_, reason) => { Assert.False(string.IsNullOrWhiteSpace(reason)); failed++; };
        view.Start();
        view.Start();
        Assert.Equal(1, failed);
        Assert.True(view.HasFailed);
        Assert.Empty(view.Children);
        Assert.Null(view.Host);
        Assert.False(view.IsReady);
        Assert.Equal("https://app.cclabs.app/embed/spiral?mode=map", view.Url);
        return Task.CompletedTask;
    });

    // WPF catches host callback failures: the canvas remains decorative even when its host throws.
    [Theory]
    [InlineData("ready")]
    [InlineData("navigated")]
    [InlineData("failed")]
    [InlineData("send")]
    public Task HostCallbackFailure_DoesNotEscapeOrResurrectDisposedView(string callback) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var view = new SpiralEmbedView("mini");
        Action action;
        switch (callback)
        {
            case "ready":
                view.Ready += (_, _) => throw new InvalidOperationException("test callback");
                action = () => view.Receive("{\"type\":\"spiral:ready\"}");
                break;
            case "navigated":
                view.Navigated += (_, _) => throw new InvalidOperationException("test callback");
                action = () => view.OnNavigated(new Uri(view.Url), true);
                break;
            case "failed":
                view.Failed += (_, _) => throw new InvalidOperationException("test callback");
                action = () => view.OnNavigated(new Uri(view.Url), false);
                break;
            default:
                view.SendJson = _ => throw new InvalidOperationException("test transport");
                view.PostState(new DescentBlock { DevotionDays = 3 });
                action = () => view.Receive("{\"type\":\"spiral:ready\"}");
                break;
        }
        Assert.Null(Record.Exception(action));
        Assert.Equal(callback == "failed", view.HasFailed);
        if (callback is "ready" or "send") Assert.True(view.IsReady);
        view.Dispose();
        Assert.Null(Record.Exception(() => view.Receive("{\"type\":\"spiral:ready\"}")));
        Assert.False(view.IsReady);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("windows")]
    [InlineData("gtk")]
    [InlineData("wpe")]
    public void NativeEnvironment_UsesSharedSpiralProfileAndDisablesDevTools(string engine)
    {
        // These are property bags with an internal DeferralManager constructor. No native adapter
        // is created: exercising the actual callback's setup must never attach to a desktop.
        var type = engine switch
        {
            "windows" => typeof(WindowsWebView2EnvironmentRequestedEventArgs),
            "gtk" => typeof(GtkWebViewEnvironmentRequestedEventArgs),
            _ => typeof(LinuxWpeWebViewEnvironmentRequestedEventArgs),
        };
        var args = (WebViewEnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(type);
        args.EnableDevTools = true;
        SpiralEmbedView.ConfigureEnvironment(args);
        var folder = Path.Combine(CorePaths.UserData, "browser_data_spiral");
        Assert.True(Directory.Exists(folder));
        Assert.False(args.EnableDevTools);
        switch (args)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs win:
                Assert.Equal(folder, win.UserDataFolder);
                Assert.Equal("--disable-direct-composition-video-overlays " +
                    "--disable-features=CalculateNativeWinOcclusion " +
                    "--disable-backgrounding-occluded-windows", win.AdditionalBrowserArguments);
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                Assert.Equal(folder, gtk.BaseDataDirectory);
                Assert.Equal(Path.Combine(folder, "cache"), gtk.BaseCacheDirectory);
                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                Assert.Equal(folder, wpe.DataDirectory);
                Assert.Equal(Path.Combine(folder, "cache"), wpe.CacheDirectory);
                break;
        }
    }

    [Fact]
    public Task NativeChildSharesTheHostsDarkSinkBeforeAttachment() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var view = new SpiralEmbedView("map");
        var host = new WebHost { Background = view.Background };
        var native = new NativeWebView(); // never added to a visual tree or attached to a desktop
        host.BindNativeBackground(native);
        Assert.Equal(Color.FromRgb(0x1C, 0x1C, 0x36), Assert.IsType<SolidColorBrush>(native.Background).Color);
        host.Background = Brushes.Black;
        Assert.Same(Brushes.Black, native.Background);
        return Task.CompletedTask;
    });

    // WPF SpiralTabView.xaml.cs:881-887: a failed embed latches _embedGaveUp, tears down and shows the
    // waiting room; a repaint in the same entry does not retry, the next entry does.
    [Fact]
    public Task TabFailedEmbed_ShowsWaitingRoom_NoEmptySlab_NoRetryUntilNextEntry() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tab = new global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView();
        var window = new global::Avalonia.Controls.Window { Content = tab };
        window.Show();
        try
        {
            var embedHost = tab.FindControl<global::Avalonia.Controls.Grid>("EmbedHost")!;
            var waiting = tab.FindControl<global::Avalonia.Controls.Border>("WaitingPanel")!;

            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.True(waiting.IsVisible);
            Assert.False(embedHost.IsVisible);
            Assert.Empty(embedHost.Children);

            // A repaint in the same entry keeps the waiting room and builds nothing.
            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.True(waiting.IsVisible);
            Assert.Empty(embedHost.Children);

            // Leaving and coming back earns a fresh attempt (which fails again, headless has no engine).
            tab.IsVisible = false;
            tab.IsVisible = true;
            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.True(waiting.IsVisible);
            Assert.False(embedHost.IsVisible);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // WPF SpiralTabView.xaml.cs embed Navigated: stop the watchdog, show the host, fade the splash.
    [Fact]
    public Task TabEmbedNavigated_RevealsHost_HidesSplash() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldEngine = global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.EngineAvailable;
        var oldStart = global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.StartEmbed;
        var oldMotion = CoreSettings.Current.MotionLevel;
        global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.EngineAvailable = () => true;
        global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.StartEmbed = _ => { };
        CoreSettings.Current.MotionLevel = MotionLevel.Off;   // no fade: the splash hides at once
        var tab = new global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView();
        var window = new global::Avalonia.Controls.Window { Content = tab };
        window.Show();
        try
        {
            var embedHost = tab.FindControl<Grid>("EmbedHost")!;
            var splash = tab.FindControl<Grid>("SpiralSplash")!;

            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.True(splash.IsVisible);
            Assert.False(embedHost.IsVisible);
            var embed = Assert.IsType<SpiralEmbedView>(Assert.Single(embedHost.Children));

            embed.OnNavigated(new Uri(embed.Url), true);
            Assert.True(embedHost.IsVisible);
            Assert.False(splash.IsVisible);
        }
        finally
        {
            window.Close();
            global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.EngineAvailable = oldEngine;
            global::ConditioningControlPanel.Avalonia.Views.Tabs.SpiralTabView.StartEmbed = oldStart;
            CoreSettings.Current.MotionLevel = oldMotion;
        }
        return Task.CompletedTask;
    });
}
