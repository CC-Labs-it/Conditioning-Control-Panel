using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
using Xunit;
namespace CCP.Avalonia.Tests;

public sealed class ChasterTrailerTests
{
    private static readonly Uri Page = new("http://127.0.0.1:12345/chaster/trailers.html");
    private static void App()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public Task LatestSceneWinsAndPauseAndShutdownStopIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        App();
        var scripts = new List<string>();
        var mount = new TaskCompletionSource<string?>();
        var first = true;
        var view = new ChasterTrailerView(Page, script =>
        {
            scripts.Add(script);
            if (script.StartsWith("window.__mount") && first) { first = false; return mount.Task; }
            return Task.FromResult<string?>("true");
        });
        view.Show("typo", 30); view.Show("program", 60, 120);
        Assert.Empty(scripts);
        var navigation = view.NavigationFinished(Page, true);
        view.Show("program", 90, 120);
        mount.SetResult("true"); await navigation;
        Assert.Equal(ChasterTrailerView.MountScript("program", 90, 120), scripts.Last());
        Assert.True(view.IsReady);
        view.Pause(); Assert.Equal(ChasterTrailerView.PauseScript, scripts.Last());
        view.Show("typo", 45); Assert.Equal(ChasterTrailerView.MountScript("typo", 45), scripts.Last());
        view.Shutdown(); var count = scripts.Count; view.Show("program");
        Assert.Equal(count, scripts.Count); Assert.False(view.IsReady);
    });
    [Fact]
    public Task BrowserFencesNavigationAndFallsBackOnFailure() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        App();
        var view = new ChasterTrailerView(Page, _ => Task.FromResult<string?>("true"));
        Assert.True(view.AllowsNavigation(Page));
        Assert.False(view.AllowsNavigation(new Uri("https://example.invalid/chaster/trailers.html")));
        Assert.False(view.AllowsNavigation(new Uri("http://127.0.0.1:12346/chaster/trailers.html")));
        Assert.False(view.AllowsNavigation(new Uri("http://127.0.0.1:12345/elsewhere.html")));
        Assert.Equal("window.__mount && window.__mount('typo',30,null)", ChasterTrailerView.MountScript("t\\y'p\"o<>", 30));
        var failures = 0; view.Failed += (_, _) => failures++;
        view.Show("typo"); await view.NavigationFinished(Page, false); view.BrowserProcessFailed();
        Assert.True(view.HasFailed); Assert.False(view.IsReady); Assert.Equal(1, failures);
        var crashed = new ChasterTrailerView(Page, _ => Task.FromResult<string?>("true"));
        crashed.Show("typo"); await crashed.NavigationFinished(Page, true); Assert.True(crashed.IsReady);
        crashed.BrowserProcessFailed(); Assert.True(crashed.HasFailed); Assert.False(crashed.IsReady);
        var badScript = new ChasterTrailerView(Page, _ => Task.FromResult<string?>(null));
        await badScript.NavigationFinished(Page, true); Assert.True(badScript.HasFailed);
        var pending = new TaskCompletionSource<string?>();
        var closing = new ChasterTrailerView(Page, _ => pending.Task);
        var navigation = closing.NavigationFinished(Page, true); closing.Shutdown(); pending.SetResult("true"); await navigation;
        Assert.False(closing.IsReady);
    });
    [Fact]
    public void BrowserEnvironmentUsesDedicatedProfileAndDisablesDevTools()
    {
        var win = (global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs));
        win.EnableDevTools = true; ChasterTrailerView.ConfigureBrowser(win);
        Assert.False(win.EnableDevTools);
        Assert.EndsWith("browser_data_chaster", win.UserDataFolder);
        Assert.Contains("--disable-features=CalculateNativeWinOcclusion", win.AdditionalBrowserArguments);
        var gtk = (global::Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(global::Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs));
        ChasterTrailerView.ConfigureBrowser(gtk); Assert.True(gtk.EphemeralDataManager);
    }

    private static Task WithTab(Func<ChasterTabView, Window, Task> body)
    {
        App();
        var tab = new ChasterTabView(); tab.BuildMenu();
        tab.FindControl<StackPanel>("MenuPanel")!.IsVisible = true;
        var window = new Window { Width = 1200, Height = 3000, Content = tab };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try { return body(tab, window); }
        finally { window.Close(); }
    }
    [Fact]
    public Task HoverDelayRetargetGraceAndPanic() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        void Hover(Control row) => w.MouseMove(row.TranslatePoint(new Point(20, row.Bounds.Height / 2), w)!.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(150), tab.TrailerOpenTimer.Interval);
        Assert.Equal(TimeSpan.FromMilliseconds(140), tab.TrailerCloseTimer.Interval);
        tab.TrailerOpenTimer.Interval = tab.TrailerCloseTimer.Interval = TimeSpan.FromHours(1);
        Hover(tab.PriceRows["typo"]); Assert.Null(tab.TrailerId); Assert.True(tab.TrailerOpenTimer.IsEnabled);
        w.MouseMove(new Point(2, 2)); Assert.False(tab.TrailerOpenTimer.IsEnabled);
        Hover(tab.PriceRows["typo"]); tab.TrailerOpenTick(); Assert.Equal("typo", tab.TrailerId);
        Hover(tab.PriceRows["session"]); Assert.Equal("session", tab.TrailerId);
        w.MouseMove(new Point(2, 2)); Assert.True(tab.TrailerCloseTimer.IsEnabled);
        var card = tab.FindControl<Border>("TrailerCard")!;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card, pointer, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        tab.TrailerCloseTick(); Assert.Equal("session", tab.TrailerId);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, card, pointer, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        tab.TrailerCloseTick(); Assert.Null(tab.TrailerId);
        tab.OpenTrailer(tab.PriceRows["typo"]);
        PanicSurfaces.All.Single(s => s.Id == "chaster-trailer").Stop(null); Assert.Null(tab.TrailerId);
        tab.OpenTrailer(tab.PriceRows["session"]); tab.IsVisible = false; Assert.Null(tab.TrailerId);
        return Task.CompletedTask;
    }));
    [Fact]
    public Task EveryRowShowsItsCopyAndTier() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        CoreSettings.Current.ChasterPriceOverrides = new();
        foreach (var price in TabPrices.All)
        {
            tab.OpenTrailer(tab.PriceRows[price.Id]);
            Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey(price.Id)), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
            Assert.Equal(TabMenuCopy.Why(price.Id, Loc.Get, ChasterTabView.ShownSeconds(price.Id)), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
            Assert.Equal(TabMenuCopy.BadgeTier(price.Gate), (tab.FindControl<Border>("TrailerBadgeHost")!.Child as TierBadge)?.Tier ?? 0);
        }
        return Task.CompletedTask;
    }));
}
