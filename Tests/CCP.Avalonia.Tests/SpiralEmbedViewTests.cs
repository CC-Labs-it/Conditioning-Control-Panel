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
    public Task StateRechecksOsMotionPreferenceWithoutOverridingUserOff() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProbe = global::ConditioningControlPanel.Avalonia.Platform.SystemMotion.AnimationsEnabled;
        var oldLevel = CoreSettings.Current.MotionLevel;
        try
        {
            bool enabled = true;
            global::ConditioningControlPanel.Avalonia.Platform.SystemMotion.AnimationsEnabled = () => enabled;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var sent = new List<string>();
            using var view = new SpiralEmbedView("map")
            { SendJson = json => { sent.Add(json); return Task.CompletedTask; } };
            view.Receive("{\"type\":\"spiral:ready\"}");
            view.PostState(null);
            Assert.False((bool)JObject.Parse(sent[^1])["reduced_motion"]!);
            enabled = false;
            view.PostState(null);
            Assert.True((bool)JObject.Parse(sent[^1])["reduced_motion"]!);
            Assert.False(AmbientFxCanvas.Env.AllowAmbientLoops);
            Assert.True(AmbientFxCanvas.Env.AllowTransitions);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            enabled = true;
            view.PostState(null);
            Assert.True((bool)JObject.Parse(sent[^1])["reduced_motion"]!);
            Assert.False(AmbientFxCanvas.Env.AllowTransitions);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.Platform.SystemMotion.AnimationsEnabled = oldProbe;
            CoreSettings.Current.MotionLevel = oldLevel;
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
