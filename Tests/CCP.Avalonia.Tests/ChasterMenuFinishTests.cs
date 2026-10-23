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
    public Task LimitsLowerNowRaiseTomorrowAndCarryTheBacklogAlong() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _, tab, w) =>
    {
        var s = CoreSettings.Current;
        var card = tab.FindControl<Border>("LimitsCard")!;
        Assert.False(card.IsVisible);
        Click(w, tab.FindControl<Button>("BtnLimits")!);
        Assert.True(card.IsVisible);
        var day = tab.FindControl<Slider>("SliderDayLimit")!;
        var backlog = tab.FindControl<Slider>("SliderBacklogLimit")!;
        var pending = tab.FindControl<TextBlock>("TxtDayPending")!;
        Assert.Equal((15d, 720d, 15d), (day.Minimum, day.Maximum, day.TickFrequency));
        Assert.Equal((60d, 2880d, 60d), (backlog.Minimum, backlog.Maximum, backlog.TickFrequency));
        day.Value = 60;
        Assert.Equal(60, s.ChasterDayLimit.Minutes);
        Assert.False(s.ChasterDayLimit.HasPending);
        Assert.Equal("60:00", tab.FindControl<TextBlock>("TxtDayLimit")!.Text);
        var before = DateTime.UtcNow;
        day.Value = 240;
        Assert.Equal(60, s.ChasterDayLimit.Minutes);
        Assert.Equal(240, s.ChasterDayLimit.PendingMinutes);
        Assert.InRange(s.ChasterDayLimit.PendingAtUtc!.Value, before.AddDays(1), DateTime.UtcNow.AddDays(1));
        Assert.True(pending.IsVisible);
        Assert.Contains("4:00:00", pending.Text);
        tab.RefreshLimits();
        Assert.Equal(240, day.Value);  // waiting figure survives revisiting
        Assert.Equal(3600, chaster.Caps.DailySeconds); // effective caption stays at today's limit
        backlog.Value = 60;
        day.Value = 300;
        Assert.Equal(300, backlog.Value);
        Assert.Equal(300, s.ChasterBacklogLimit.PendingMinutes);
        s.ChasterDayLimit = new LimitSetting(60, 300, DateTime.UtcNow.AddSeconds(-1));
        tab.RefreshLimits();
        Assert.False(pending.IsVisible);
        Assert.Equal("5:00:00", tab.FindControl<TextBlock>("TxtDayLimit")!.Text);
        Click(w, tab.FindControl<CheckBox>("ChkRelock")!);
        Assert.True(s.ChasterRelockPastEnd);
        Click(w, tab.FindControl<CheckBox>("ChkRelock")!);
        Assert.False(s.ChasterRelockPastEnd);
        Click(w, tab.FindControl<Button>("BtnLimits")!);
        Assert.False(card.IsVisible);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task AKeyRefreshesTheLimitSlidersAndTheirPendingLines() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, tab, w) =>
    {
        var s = CoreSettings.Current;
        s.ChasterDayLimit = new LimitSetting(15, 0, null);
        s.ChasterBacklogLimit = new LimitSetting(60, 0, null);
        Click(w, tab.FindControl<ToggleButton>("BtnPresetStrict")!);
        Assert.Equal(s.ChasterDayLimit.PendingMinutes, tab.FindControl<Slider>("SliderDayLimit")!.Value);
        Assert.True(tab.FindControl<TextBlock>("TxtDayPending")!.IsVisible);
        Assert.Equal(s.ChasterBacklogLimit.PendingMinutes, tab.FindControl<Slider>("SliderBacklogLimit")!.Value);
        await Task.CompletedTask;
    }));

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
    public Task PaperTagShowsDebtCreditAndPauseAndOpensTheBill() => AvaloniaTestDispatcher.RunAsync(() => Run(async (_, _, tab, w) =>
    {
        Assert.NotNull(tab.FindControl<Image>("HeroArt")!.Source);
        Assert.NotNull(tab.FindControl<global::Avalonia.Controls.Shapes.Path>("SpiralPath")!.Data);
        Assert.Equal(ConditioningControlPanel.Avalonia.Controls.AmbientFxLayers.FogDrift | ConditioningControlPanel.Avalonia.Controls.AmbientFxLayers.DustField,
            tab.FindControl<ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas>("Ambient")!.Layers);
        Assert.Equal(4, tab.FindControl<WrapPanel>("FootRow")!.Children.Count);
        Assert.True(tab.FindControl<Panel>("PaperTag")!.IsVisible);
        tab.RefreshTag(0);
        Assert.Equal(Loc.Get("chaster_tag_clear"), tab.FindControl<TextBlock>("TxtTagStamp")!.Text);
        var today = DateTime.Now;
        tab.BuildCalendar((today.AddDays(-1), today.AddDays(3)), today);
        tab.RefreshTag(120);
        Assert.Equal("+2:00", tab.FindControl<TextBlock>("TxtTagAmount")!.Text);
        Assert.Equal(Loc.Get("chaster_tag_unpaid"), tab.FindControl<TextBlock>("TxtTagStamp")!.Text);
        Assert.True(tab.FindControl<Border>("CalendarTag")!.IsVisible);
        CoreSettings.Current.ChasterPaused = true;
        tab.RefreshTag(120);
        Assert.Equal(Loc.Get(TabPageText.Tag(120, 120, true, true).Key), tab.FindControl<TextBlock>("TxtTagLands")!.Text);
        tab.RefreshTag(-60);
        Assert.Equal(Loc.Get("chaster_tag_credit"), tab.FindControl<TextBlock>("TxtTagStamp")!.Text);
        Assert.False(tab.FindControl<Border>("CalendarTag")!.IsVisible);
        tab.BuildCalendar(null, today);
        tab.RefreshTag(120);
        Assert.False(tab.FindControl<Border>("CalendarTag")!.IsVisible);
        Click(w, tab.FindControl<Panel>("PaperTag")!);
        Assert.True(tab.FindControl<Border>("ReceiptHost")!.IsVisible);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task RaffleMarksCountedDaysInPlaceAndDropsThePreviousAccountsDays() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, _, tab, _) =>
    {
        var today = DateTime.UtcNow.Date;
        tab.BuildCalendar((today.AddDays(-1), today.AddDays(2)), today);
        var grid = tab.FindControl<UniformGrid>("Calendar")!;
        var square = grid.Children[1];
        tab.ShowRaffleCard(new RaffleCard(today.ToString("yyyy-MM"), DateTime.DaysInMonth(today.Year, today.Month), today.Day,
            new[] { today.Day, today.Day + 1 }, 300, 25, 3600, false, false, null));
        Assert.Same(square, grid.Children[1]);
        Assert.Single(square.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>().Where(e => Equals(e.Tag, "raffle-day")));
        Assert.DoesNotContain(grid.Children[2].GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>(), e => Equals(e.Tag, "raffle-day"));
        Assert.True(tab.FindControl<TextBlock>("CalendarRaffleKey")!.IsVisible);
        Assert.Contains(Loc.Get("chaster_cal_raffle_key"), ToolTip.GetTip(square) as string);
        chaster.ForgetRaffle();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(square, grid.Children[1]);
        Assert.DoesNotContain(square.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>(), e => Equals(e.Tag, "raffle-day"));
        Assert.False(tab.FindControl<TextBlock>("CalendarRaffleKey")!.IsVisible);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task LockChangesRefreshNumbersAndTheNothingCountsHintReachesTheKeys() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, fake, tab, w) =>
    {
        Assert.True(chaster.Note("attention").Booked);
        Dispatcher.UIThread.RunJobs();
        var balance = tab.FindControl<TextBlock>("TxtBalance")!;
        balance.Text = "stale";
        fake.Locks = Locked;
        await chaster.RefreshLockAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CircesTab.Format(chaster.BalanceSeconds), balance.Text);
        CoreSettings.Current.ChasterPrices = new();
        tab.RefreshHero();
        tab.Height = 700;   // a short page: the keys sit below the fold (a headless window may not resize after Show)
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();   // lay out and render the new hint and viewport before the click hit-tests
        var scroll = tab.FindControl<ScrollViewer>("PageScroll")!;
        Assert.True(tab.FindControl<UniformGrid>("PresetRow")!.TranslatePoint(default, scroll)!.Value.Y > scroll.Viewport.Height);
        var hint = tab.FindControl<TextBlock>("SetupHint")!;
        Assert.Equal(Loc.Get("chaster_setup_nothing"), hint.Text);
        Assert.NotNull(hint.Cursor);
        var released = 0;
        hint.AddHandler(InputElement.PointerReleasedEvent, (_, _) => released++, global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Click(w, hint);
        Assert.Equal(1, released);   // the click landed on the hint, gaps between its letters included (WPF hit-tests a TextBlock's whole box)
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();   // the scroll lands on the next layout pass
        Assert.True(scroll.Offset.Y > 0);
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
