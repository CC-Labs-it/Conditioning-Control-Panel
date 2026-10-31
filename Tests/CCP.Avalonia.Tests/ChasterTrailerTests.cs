using System;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChasterTrailerTests
{
    private static void App()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

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
        Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey("session")), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
        Assert.Same(tab.PriceRows["session"], tab.FindControl<Popup>("Trailer")!.PlacementTarget);
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
    public Task EveryRowHasItsArtCopyTierAndBookedFigure() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        foreach (var price in TabPrices.All)
        {
            tab.OpenTrailer(tab.PriceRows[price.Id]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(price.Id, tab.TrailerId);
            Assert.NotNull(tab.FindControl<Image>("TrailerArt")!.Source);
            var popup = tab.FindControl<Popup>("Trailer")!;
            Assert.Same(tab.PriceRows[price.Id], popup.PlacementTarget);
            Assert.Equal(PlacementMode.Bottom, popup.Placement);
            Assert.Equal(320, tab.FindControl<Border>("TrailerCard")!.Width);
            Assert.Equal(190, tab.FindControl<Grid>("TrailerPlate")!.Height);
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
        return Task.CompletedTask;
    }));

    [Fact]
    public Task EditAndResetRefreshTheOpenCard() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        tab.OpenTrailer(tab.PriceRows["typo"]);
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "2:00";
        tab.CommitPriceEdit();
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, 120), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        Dispatcher.UIThread.RunJobs();
        var layer = AdornerLayer.GetAdornerLayer(tab.FindControl<Border>("TrailerFigureAnchor")!)!;
        Assert.Equal(BookedFlashPlan.For("typo", 120, AmbientFxCanvas.Env.Level)!.Value.Text,
            layer.Children.OfType<ChasterBookedFlash>().Single().Figure.Text);
        tab.FindControl<Button>("BtnResetPrices")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, ChasterTabView.ShownSeconds("typo")), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task DriftRespectsMotionGateAndWpfPhase() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        tab.OpenTrailer(tab.PriceRows["typo"]);
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
        Assert.Equal(6 * Math.Cos(Math.PI * 7 / 9.1), ((TranslateTransform)transforms.Children[1]).X, 6);
        return Task.CompletedTask;
    }));

    [Theory]
    [InlineData("hide")]
    [InlineData("detach")]
    [InlineData("unlink")]
    [InlineData("panic")]
    public Task LeavingStopsOpenCardsAndPendingHover(string reason) => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        void Leave()
        {
            if (reason == "hide") tab.IsVisible = false;
            else if (reason == "detach") w.Content = null;
            else if (reason == "unlink") tab.OnTabShown(); // no linked service in this isolated test
            else PanicSurfaces.All.Single(s => s.Id == "chaster-trailer").Stop(null);
        }
        tab.OpenTrailer(tab.PriceRows["typo"]);
        Assert.Equal(TimeSpan.FromMilliseconds(1700), tab.TrailerFigureTimer.Interval);
        Leave();
        Assert.Null(tab.TrailerId);
        Assert.False(tab.FindControl<Popup>("Trailer")!.IsOpen);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        if (reason == "hide") tab.IsVisible = true;
        if (reason == "detach") w.Content = tab;
        tab.TrailerOpenTimer.Interval = TimeSpan.FromHours(1);
        tab.TrailerOpenTimer.Start();
        Leave();
        Assert.False(tab.TrailerOpenTimer.IsEnabled);
        Assert.False(tab.TrailerCloseTimer.IsEnabled);
        return Task.CompletedTask;
    }));

}
