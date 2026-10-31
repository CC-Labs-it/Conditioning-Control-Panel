using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class ChasterTrailerTests {
    private static void App() {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options(systemEmoji: false)).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static Task WithTab(Func<ChasterTabView, Window, Task> body) {
        App();
        return ChasterMenuTests.Run((_, _, tab, w) => {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreSettings.Current.PerformanceMode = CoreSettings.Current.AutoPerformanceMode = false;
            return body(tab, w);
        });
    }
    [Fact]
    public Task HoverWaitsThenRetargetsImmediatelyAndClosesAfterTheGrace() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) => {
        void Hover(Control row) => w.MouseMove(row.TranslatePoint(new Point(20, row.Bounds.Height / 2), w)!.Value);
        Assert.Equal(320, tab.FindControl<Border>("TrailerCard")!.Width);
        Assert.Equal(190, tab.FindControl<Grid>("TrailerPlate")!.Height);
        Assert.Equal(PlacementMode.Bottom, tab.FindControl<Popup>("Trailer")!.Placement);
        var open = tab.TrailerOpenTimer;
        var close = tab.TrailerCloseTimer;
        Assert.Equal(TimeSpan.FromMilliseconds(150), open.Interval); // WPF ChasterTabView.xaml.cs open delay
        Assert.Equal(TimeSpan.FromMilliseconds(140), close.Interval); // WPF close grace
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
        Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey("session")), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
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
        Assert.Null(tab.TrailerId); Assert.False(tab.TrailerFigureTicking); Assert.False(tab.TrailerDrifting);
        return Task.CompletedTask;
    }));
    [Fact]
    public Task EveryRowHasItsCopyTierAndLiveFiguresAndEditResetRefreshTheCard() => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) => {
        foreach (var price in TabPrices.All) {
            tab.OpenTrailer(tab.PriceRows[price.Id]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(price.Id, tab.TrailerId);
            if (TabMenuCopy.ArtFor(price.Id) != null) Assert.NotNull(tab.FindControl<Image>("TrailerArt")!.Source);
            Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey(price.Id)), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
            Assert.Equal(TabMenuCopy.Why(price.Id, Loc.Get, ChasterTabView.ShownSeconds(price.Id)), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
            var tier = tab.FindControl<Border>("TrailerBadgeHost")!.Child as TierBadge;
            Assert.Equal(TabMenuCopy.BadgeTier(price.Gate), tier?.Tier ?? 0);
            Assert.True(tab.TrailerFigureTicking);
            if (BookedFlashPlan.For(price.Id, price.Seconds, AmbientFxCanvas.Env.Level) is { } plan) {
                var layer = AdornerLayer.GetAdornerLayer(tab.FindControl<Border>("TrailerFigureAnchor")!)!;
                Assert.Equal(plan.Text, Assert.Single(layer.Children.OfType<ChasterBookedFlash>()).Figure.Text);
            }
        }
        tab.OpenTrailer(tab.PriceRows["typo"]);
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "2:00";
        tab.CommitPriceEdit();
        Dispatcher.UIThread.RunJobs();
        var editedLayer = AdornerLayer.GetAdornerLayer(tab.FindControl<Border>("TrailerFigureAnchor")!)!;
        Assert.Equal(BookedFlashPlan.For("typo", 120, AmbientFxCanvas.Env.Level)!.Value.Text,
            Assert.Single(editedLayer.Children.OfType<ChasterBookedFlash>()).Figure.Text);
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, 120), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        tab.FindControl<Button>("BtnResetPrices")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, ChasterTabView.ShownSeconds("typo")), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        Assert.Equal(TimeSpan.FromMilliseconds(1700), tab.TrailerFigureTimer.Interval);
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
        tab.AdvanceTrailerDrift(9.1);
        Assert.Equal(-6, ((TranslateTransform)transforms.Children[1]).X, 6);
        tab.IsVisible = false;
        Assert.Null(tab.TrailerId); Assert.False(tab.TrailerFigureTicking); Assert.False(tab.TrailerDrifting);
        tab.IsVisible = true;
        tab.OpenTrailer(tab.PriceRows["session"]);
        PanicSurfaces.All.Single(s => s.Id == "chaster-trailer").Stop(null);
        Assert.Null(tab.TrailerId); Assert.False(tab.TrailerFigureTicking); Assert.False(tab.TrailerDrifting);
        tab.OpenTrailer(tab.PriceRows["typo"]);
        await ChasterHead.Service!.UnlinkAsync();
        tab.Refresh(); // unlink hides the menu and trailer
        Assert.Null(tab.TrailerId);
        tab.OpenTrailer(tab.PriceRows["session"]);
        w.Content = null; // detach stops both timers and ambient work
        Assert.Null(tab.TrailerId); Assert.False(tab.TrailerFigureTicking); Assert.False(tab.TrailerDrifting);
        await Task.CompletedTask;
    }));
    // WPF ChasterTabView.xaml.cs:1434-1437, PriceEdit.cs:158; Controls/ChasterTrailerView.cs:181-209.
    [Fact]
    public Task ScenesFollowLatestHoverAndEditedPrices() => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) => {
        var view = tab.FindControl<ChasterTrailerView>("TrailerWeb")!;
        var scripts = new List<string>(); var pending = new TaskCompletionSource<string?>();
        var host = new ConditioningControlPanel.Avalonia.Views.Controls.WebHost { ScriptInvoker = script =>
        { scripts.Add(script); return script.StartsWith("window.__mount") && scripts.Count == 2 ? pending.Task : Task.FromResult<string?>("true"); } };
        view.Browser = host;
        tab.OpenTrailer(tab.PriceRows["typo"]); tab.OpenTrailer(tab.PriceRows["program_done"]);
        Assert.Empty(scripts);
        var navigation = view.NavigationFinished(view.Page, true);
        var figures = TabMenuCopy.SceneFigures("program_done", ChasterTabView.ShownSeconds);
        Assert.Equal(ChasterTrailerView.MountScript(TabMenuCopy.VignetteFor("program_done"), figures.Add, figures.Sub), scripts[1]);
        Assert.False(view.IsReady); Assert.False(view.IsVisible);
        tab.OpenTrailer(tab.PriceRows["typo"]); Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "2:00"; tab.CommitPriceEdit();
        pending.SetResult("true"); await navigation;
        Assert.Equal(3, scripts.Count);
        Assert.Equal(ChasterTrailerView.MountScript(TabMenuCopy.VignetteFor("typo"), 120, 60), scripts[2]);
        Assert.True(view.IsReady); Assert.True(view.IsVisible);
        Assert.True(host.AllowNavigation!(view.Page!)); Assert.False(host.AllowNavigation(new Uri("https://example.invalid/")));
        Assert.True(host.SuppressNewWindows);
        tab.HideTrailer(); Assert.False(view.IsVisible); Assert.Equal(ChasterTrailerView.PauseScript, scripts.Last());
    }));
    [Theory]
    [InlineData(false, "true")]
    [InlineData(true, "false")]
    [InlineData(true, null)]
    [InlineData(true, "throw")]
    public Task BrowserFailureKeepsTheArtwork(bool navigationOk, string? result) => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) => {
        var view = tab.FindControl<ChasterTrailerView>("TrailerWeb")!;
        view.Browser = new ConditioningControlPanel.Avalonia.Views.Controls.WebHost { ScriptInvoker = script =>
            !script.StartsWith("window.__mount") ? Task.FromResult<string?>("true") : result == "throw" ? Task.FromException<string?>(new InvalidOperationException()) : Task.FromResult(result) };
        tab.OpenTrailer(tab.PriceRows["typo"]);
        view.Browser.OnNavigationCompleted(view.Page, navigationOk);
        Assert.True(view.HasFailed); Assert.False(view.IsVisible); Assert.False(view.IsReady);
        Assert.NotNull(tab.FindControl<Image>("TrailerArt")!.Source);
        tab.OpenTrailer(tab.PriceRows["program_done"]); Assert.True(view.HasFailed); Assert.False(view.IsVisible);
        await Task.CompletedTask;
    }));
    [Fact]
    public Task HidingDuringMountStopsTheSceneAndNeverRevealsIt() => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) => {
        var view = tab.FindControl<ChasterTrailerView>("TrailerWeb")!; var scripts = new List<string>();
        var pending = new TaskCompletionSource<string?>();
        view.Browser = new ConditioningControlPanel.Avalonia.Views.Controls.WebHost { ScriptInvoker = script =>
        { scripts.Add(script); return script.StartsWith("window.__mount") ? pending.Task : Task.FromResult<string?>("true"); } };
        tab.OpenTrailer(tab.PriceRows["typo"]); var navigation = view.NavigationFinished(view.Page, true);
        tab.HideTrailer(); pending.SetResult("true"); await navigation;
        Assert.False(view.IsVisible); Assert.Equal(ChasterTrailerView.PauseScript, scripts.Last());
    }));
    [Theory, InlineData(true), InlineData(false)]
    public void NativeOptionsMatchTheTrailerProfile(bool windows) {
        var args = (WebViewEnvironmentRequestedEventArgs)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(windows ? typeof(global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs) : typeof(global::Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs));
        args.EnableDevTools = true; ChasterTrailerView.ConfigureBrowser(args); Assert.False(args.EnableDevTools);
        if (args is global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs win)
        { Assert.Equal(Path.Combine(CorePaths.UserData, "browser_data_chaster"), win.UserDataFolder); Assert.Equal("--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows", win.AdditionalBrowserArguments); }
        else Assert.True(((global::Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs)args).EphemeralDataManager);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReopeningDuringPauseDrainsTheLatestScene(bool alreadyReady) => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) => {
        var view = tab.FindControl<ChasterTrailerView>("TrailerWeb")!; var scripts = new List<string>();
        var mount = new TaskCompletionSource<string?>(); var pause = new TaskCompletionSource<string?>();
        view.Browser = new ConditioningControlPanel.Avalonia.Views.Controls.WebHost { ScriptInvoker = script => {
            scripts.Add(script); return script == ChasterTrailerView.PauseScript ? pause.Task : script.StartsWith("window.__mount") && !alreadyReady && scripts.Count == 2 ? mount.Task : Task.FromResult<string?>("true"); } };
        tab.OpenTrailer(tab.PriceRows["typo"]); var navigation = view.NavigationFinished(view.Page, true);
        if (alreadyReady) await navigation;
        tab.HideTrailer(); mount.SetResult("true"); await Task.Yield();
        Assert.Equal(ChasterTrailerView.PauseScript, scripts.Last());
        tab.OpenTrailer(tab.PriceRows["program_done"]); Assert.False(view.IsVisible);
        pause.SetResult("true"); await navigation; await Task.Yield();
        Assert.Equal(ChasterTrailerView.MountScript("program", 1800, 600), scripts.Last());
        Assert.True(view.IsVisible); Assert.True(view.IsReady);
    }));
    [Theory, InlineData(false), InlineData(true)]
    public Task ProcessFailureBeforeOrAfterReadinessImmediatelyRestoresArtwork(bool completed) => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) => {
        var view = tab.FindControl<ChasterTrailerView>("TrailerWeb")!;
        var host = new ConditioningControlPanel.Avalonia.Views.Controls.WebHost { ScriptInvoker = _ => Task.FromResult<string?>("true") };
        Action? crash = null;
        host.FailurePolicy = (_, failed) => { Assert.Null(host.ScrolllerGate); crash = failed; return new TrailerBrowserPolicy(() => { }); };
        view.Browser = host; tab.OpenTrailer(tab.PriceRows["typo"]);
        var ready = host.NewScrolllerEngine(); ready(true); // core ready, before navigation
        Assert.NotNull(crash); Assert.Null(host.CurrentUrl);
        var watch = crash; ready(true); Assert.Same(watch, crash);
        if (completed) await view.NavigationFinished(view.Page, true);
        Assert.Equal(completed, view.IsVisible); var failures = 0; view.Failed += (_, _) => failures++;
        crash!(); Assert.True(view.HasFailed); Assert.False(view.IsReady); Assert.False(view.IsVisible); Assert.Empty(view.Children);
        Assert.NotNull(tab.FindControl<Image>("TrailerArt")!.Source);
        await view.NavigationFinished(view.Page, true); // a late completion cannot revive a crashed player
        crash!(); Assert.Equal(1, failures); Assert.False(view.IsVisible); Assert.Empty(view.Children);
    }));
    [Fact]
    public Task FailureSubscriptionErrorKeepsNavigationHeld() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) => {
        var view = tab.FindControl<ChasterTrailerView>("TrailerWeb")!;
        view.Browser = new ConditioningControlPanel.Avalonia.Views.Controls.WebHost { ScriptInvoker = _ => Task.FromResult<string?>("true"),
            FailurePolicy = (_, _) => throw new InvalidOperationException("no native core") };
        tab.OpenTrailer(tab.PriceRows["typo"]); view.Browser.NewScrolllerEngine()(true);
        Assert.Null(view.Browser.ScrolllerGate); Assert.True(view.HasFailed); Assert.Empty(view.Children);
        return Task.CompletedTask;
    }));

}
