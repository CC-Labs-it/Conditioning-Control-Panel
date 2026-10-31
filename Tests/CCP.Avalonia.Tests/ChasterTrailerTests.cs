using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class ChasterMenuTests
{
    [Fact]
    public Task TrailerShowsEachRowsLiveFigureAndRefreshesAfterEditAndReset() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, tab, w) =>
    {
        foreach (var price in TabPrices.All)
        {
            tab.OpenTrailer(tab.PriceRows[price.Id]);
            Assert.Equal(price.Id, tab.TrailerId);
            Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey(price.Id)), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
            Assert.Equal(TabMenuCopy.Why(price.Id, Loc.Get, price.Seconds), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
            Assert.Equal(TabMenuCopy.BadgeTier(price.Gate) > 0, tab.FindControl<Border>("TrailerBadgeHost")!.Child != null);
            Assert.Equal(TabMenuCopy.HasFixedFigure(price.Id) ? TabPageText.Price(price, Loc.Get("chaster_each")) : "",
                tab.FindControl<TextBlock>("TxtTrailerFigure")!.Text);
        }
        tab.OpenTrailer(tab.PriceRows["typo"]);
        Assert.True(tab.FindControl<Popup>("Trailer")!.IsOpen);
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "2";
        tab.CommitPriceEdit();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();   // the reset link just appeared; clicks hit-test the last rendered frame
        Assert.Contains("+2:00", tab.FindControl<TextBlock>("TxtTrailerFigure")!.Text);
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, 120), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        Click(w, tab.FindControl<Button>("BtnResetPrices")!);
        Assert.Contains("+0:30", tab.FindControl<TextBlock>("TxtTrailerFigure")!.Text);
        tab.IsVisible = false;
        Assert.Null(tab.TrailerId);
        Assert.False(tab.FindControl<Popup>("Trailer")!.IsOpen);
        Assert.False(tab.TrailerOpenPending);
        Assert.False(tab.TrailerClosePending);
        tab.IsVisible = true;
        tab.OpenTrailer(tab.PriceRows["typo"]);
        w.Content = null;
        Assert.Null(tab.TrailerId);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task HoverWaitsThenOpensAndLeavingClosesAfterTheGrace() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, tab, w) =>
    {
        var row = tab.PriceRows["typo"];
        var p = row.TranslatePoint(new Point(10, 10), w)!.Value;
        // Stepped timers (P08): assert which wait is armed, then run it out by hand.
        Assert.Equal((TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(140)), tab.TrailerDelays);
        tab.TrailerDelays = (TimeSpan.FromHours(1), TimeSpan.FromHours(1));   // headless input runs due timers; nothing may come due by itself
        w.MouseMove(new Point(0, 0));
        tab.HideTrailer();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        w.MouseMove(p);
        Assert.Null(tab.TrailerId);
        Assert.True(tab.TrailerOpenPending);
        tab.TrailerOpenElapsed();
        Assert.Equal("typo", tab.TrailerId);
        w.MouseMove(new Point(0, 0));
        Assert.Equal("typo", tab.TrailerId);
        Assert.True(tab.TrailerClosePending);
        var card = tab.FindControl<Border>("TrailerCard")!;
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card, null!, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.False(tab.TrailerClosePending);
        tab.TrailerCloseElapsed();   // a grace already in flight must not close a card the pointer is on
        Assert.Equal("typo", tab.TrailerId);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, card, null!, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.True(tab.TrailerClosePending);
        tab.TrailerCloseElapsed();
        Assert.Null(tab.TrailerId);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task UnlinkDismissesTheTrailer() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _, tab, _) =>
    {
        tab.OpenTrailer(tab.PriceRows["typo"]);
        Assert.NotNull(tab.TrailerId);
        await chaster.UnlinkAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(tab.TrailerId);
        Assert.False(tab.FindControl<Popup>("Trailer")!.IsOpen);
    }));

    [Fact]
    public Task PanicDismissesTheTrailer() => AvaloniaTestDispatcher.RunAsync(() => ChasterBillTests.Run(async (_, shell) =>
    {
        shell.ShowTab("chaster");
        var tab = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.ChasterTabView>("ChasterTab")!;
        tab.OpenTrailer(tab.PriceRows.Values.First());
        Assert.NotNull(tab.TrailerId);
        PanicSurfaces.All.Single(s => s.Id == "chaster-trailer").Stop(shell);
        Assert.Null(tab.TrailerId);
        await Task.CompletedTask;
    }));
}
