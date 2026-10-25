using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
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
    public Task BrowserQueuesTheLatestSceneIncludingAnEditDuringTheFirstMount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        App();
        var scripts = new List<string>();
        var mounting = new TaskCompletionSource<string?>();
        var first = true;
        var view = new ChasterTrailerView(Page, script =>
        {
            scripts.Add(script);
            if (script.StartsWith("window.__mount") && first) { first = false; return mounting.Task; }
            return Task.FromResult<string?>("true");
        });
        var ready = 0;
        view.Ready += (_, _) => ready++;
        view.Show("typo", 30, null);
        view.Show("program", 60, 120);
        Assert.Empty(scripts); // nothing executes before navigation
        var navigation = view.NavigationFinished(Page, true);
        Assert.False(view.IsReady);
        view.Show("program", 90, 120); // same scene, edited figures while mount awaits
        mounting.SetResult("true");
        await navigation;
        Assert.Equal(ChasterTrailerView.MountScript("program", 60, 120), scripts[1]);
        Assert.Equal(ChasterTrailerView.MountScript("program", 90, 120), scripts[2]);
        Assert.True(view.IsReady);
        Assert.Equal(1, ready);
        view.Pause();
        Assert.Equal(ChasterTrailerView.PauseScript, scripts.Last());
        view.Show("typo", 45);
        Assert.Equal(ChasterTrailerView.MountScript("typo", 45), scripts.Last());
        view.Shutdown();
        var count = scripts.Count;
        view.Show("program");
        view.Start();
        Assert.Equal(count, scripts.Count);
        Assert.False(view.IsReady);
    });

    [Theory]
    [InlineData(false, "true")]
    [InlineData(true, "false")]
    [InlineData(true, null)]
    public Task NavigationAndScriptFailuresExposeTheStillPictureOnce(bool navigationOk, string? result) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        App();
        var view = new ChasterTrailerView(Page, _ => Task.FromResult(result));
        var failures = 0;
        view.Failed += (_, _) => failures++;
        view.Show("typo");
        await view.NavigationFinished(Page, navigationOk);
        await view.NavigationFinished(Page, false);
        view.Show("program");
        Assert.True(view.HasFailed);
        Assert.False(view.IsReady);
        Assert.Equal(1, failures);
        view.Shutdown();
    });

    [Fact]
    public Task MissingEngineAndShutdownDuringNavigationNeverPublishReady() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        App();
        var fallback = new ChasterTrailerView();
        fallback.Start(); // WebHost never constructs a native engine under headless
        Assert.True(fallback.HasFailed);
        Assert.Empty(fallback.Children);
        var pending = new TaskCompletionSource<string?>();
        var view = new ChasterTrailerView(Page, _ => pending.Task);
        var ready = 0;
        view.Ready += (_, _) => ready++;
        view.Start();
        var navigation = view.NavigationFinished(Page, true);
        view.Shutdown();
        pending.SetResult("true");
        await navigation;
        Assert.Equal(0, ready);
        Assert.False(view.IsReady);
    });

    [Fact]
    public Task NavigationFenceAndEscapingMatchTheReference() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        App();
        var view = new ChasterTrailerView(Page, _ => Task.FromResult<string?>("true"));
        Assert.True(view.AllowsNavigation(Page));
        Assert.False(view.AllowsNavigation(new Uri("https://example.invalid/chaster/trailers.html")));
        Assert.False(view.AllowsNavigation(new Uri("http://127.0.0.1:12346/chaster/trailers.html")));
        Assert.False(view.AllowsNavigation(new Uri("http://127.0.0.1:12345/elsewhere.html")));
        Assert.Equal("window.__mount && window.__mount('typo',30,null)", ChasterTrailerView.MountScript("t\\y'p\"o<>", 30));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ProcessFailureImmediatelyRestoresFallbackDuringMountOrAfterReady(bool afterReady) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        App();
        var mount = new TaskCompletionSource<string?>();
        var host = new WebHost(); // headless: no native browser or persistent profile
        var view = new ChasterTrailerView(Page, script => script.StartsWith("window.__mount")
            ? mount.Task : Task.FromResult<string?>("true"));
        view.ObserveBrowser(host);
        var failures = 0;
        var ready = 0;
        view.Failed += (_, _) => failures++;
        view.Ready += (_, _) => ready++;
        view.Show("typo", 30);
        var navigation = view.NavigationFinished(Page, true);
        if (afterReady) { mount.SetResult("true"); await navigation; Assert.True(view.IsReady); }
        host.OnBrowserProcessFailed(); // same signal used by the native adapters, without another hover/mount
        Assert.True(view.HasFailed);
        Assert.False(view.IsReady);
        Assert.Equal(1, failures);
        if (!afterReady) { mount.SetResult("true"); await navigation; Assert.Equal(0, ready); }
        host.OnBrowserProcessFailed();
        Assert.Equal(1, failures);
        view.Shutdown();
    });

    [Fact]
    public Task ProcessFailureAfterShutdownDoesNotNotifyOrRestart() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        App();
        var host = new WebHost();
        var view = new ChasterTrailerView(Page, _ => Task.FromResult<string?>("true"));
        view.ObserveBrowser(host);
        var failures = 0;
        view.Failed += (_, _) => failures++;
        view.Shutdown();
        host.OnBrowserProcessFailed();
        Assert.Equal(0, failures);
        Assert.False(view.IsReady);
        return Task.CompletedTask;
    });

    private static async Task WithTab(Func<ChasterTabView, Window, Task> body)
    {
        App();
        var settings = CoreSettings.Current;
        var overrides = settings.ChasterPriceOverrides;
        var motion = (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode);
        (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = (MotionLevel.Full, false, false);
        settings.ChasterPriceOverrides = new();
        var tab = new ChasterTabView();
        tab.BuildMenu();
        tab.FindControl<StackPanel>("MenuPanel")!.IsVisible = true;
        var window = new Window { Width = 1200, Height = 3000, Content = tab };
        try { window.Show(); Dispatcher.UIThread.RunJobs(); await body(tab, window); }
        finally
        {
            window.Close(); settings.ChasterPriceOverrides = overrides;
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = motion;
        }
    }

    [Fact]
    public Task HoverWaitsThenRetargetsImmediatelyAndClosesAfterTheGrace() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        void Hover(Control row) => w.MouseMove(row.TranslatePoint(new Point(20, row.Bounds.Height / 2), w)!.Value);
        var open = tab.TrailerOpenTimer;
        var close = tab.TrailerCloseTimer;
        Assert.Equal(TimeSpan.FromMilliseconds(150), open.Interval); // WPF ChasterTabView.xaml.cs open delay
        Assert.Equal(TimeSpan.FromMilliseconds(140), close.Interval); // WPF close grace
        // A slow first layout inside MouseMove can outlast 150 ms on CI and fire a real tick: park the
        // timers so only the explicit ticks below advance the state (P08).
        open.Interval = close.Interval = TimeSpan.FromHours(1);
        Hover(tab.PriceRows["typo"]);
        Assert.Null(tab.TrailerId);
        Assert.True(open.IsEnabled);
        w.MouseMove(new Point(2, 2));
        Assert.False(open.IsEnabled); // a brief pass over a row never opens the card
        Assert.True(close.IsEnabled);
        tab.TrailerCloseTick();
        Assert.Null(tab.TrailerId);
        Hover(tab.PriceRows["typo"]);
        Assert.True(open.IsEnabled);
        tab.TrailerOpenTick();
        Assert.Equal("typo", tab.TrailerId);
        Hover(tab.PriceRows["session"]);
        Assert.Equal("session", tab.TrailerId);
        Assert.False(open.IsEnabled); // an open card switches rows without the delay
        w.MouseMove(new Point(2, 2));
        Assert.Equal("session", tab.TrailerId);
        Assert.True(close.IsEnabled);
        var card = tab.FindControl<Border>("TrailerCard")!;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card, pointer, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.False(close.IsEnabled); // crossing the gap onto the card cancels close
        tab.TrailerCloseTick(); // even a tick already queued keeps the card while the pointer is on it
        Assert.Equal("session", tab.TrailerId);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, card, pointer, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.True(close.IsEnabled);
        Assert.Equal("session", tab.TrailerId);
        tab.TrailerCloseTick();
        Assert.Null(tab.TrailerId);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task EveryRowHasItsCopyTierAndLiveFiguresAndEditResetRefreshTheCard() => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) =>
    {
        foreach (var price in TabPrices.All)
        {
            tab.OpenTrailer(tab.PriceRows[price.Id]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(price.Id, tab.TrailerId);
            Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey(price.Id)), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
            Assert.Equal(TabMenuCopy.Why(price.Id, Loc.Get, ChasterTabView.ShownSeconds(price.Id)), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
            var tier = tab.FindControl<Border>("TrailerBadgeHost")!.Child as TierBadge;
            Assert.Equal(TabMenuCopy.BadgeTier(price.Gate), tier?.Tier ?? 0);
            Assert.True(tab.TrailerFigureTicking);
            if (BookedFlashPlan.For(price.Id, price.Seconds, AmbientFxCanvas.Env.Level) is { } plan)
            {
                var layer = AdornerLayer.GetAdornerLayer(tab.FindControl<Border>("TrailerFigureAnchor")!)!;
                Assert.Equal(plan.Text, layer.Children.OfType<ChasterBookedFlash>().Single().Figure.Text);
            }
        }
        tab.OpenTrailer(tab.PriceRows["typo"]);
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "2:00";
        tab.CommitPriceEdit();
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, 120), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        tab.FindControl<Button>("BtnResetPrices")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, ChasterTabView.ShownSeconds("typo")), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        Assert.True(tab.TrailerDrifting);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(tab.TrailerDrifting);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.True(tab.TrailerDrifting);
        tab.AdvanceTrailerDrift(7);
        var transforms = (TransformGroup)tab.FindControl<Image>("TrailerArt")!.RenderTransform!;
        Assert.Equal(1.09, ((ScaleTransform)transforms.Children[0]).ScaleX, 6);
        tab.IsVisible = false;
        Assert.Null(tab.TrailerId);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        tab.IsVisible = true;
        tab.OpenTrailer(tab.PriceRows["session"]);
        PanicSurfaces.All.Single(s => s.Id == "chaster-trailer").Stop(null);
        Assert.Null(tab.TrailerId);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        await Task.CompletedTask;
    }));
}
