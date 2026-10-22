using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChasterBillboardSync7Tests
{
    [Fact]
    public Task RaffleDotsFollowTheCardInPlaceAndDropWithTheAccount() => Run(() =>
        ChasterBillTests.Run((chaster, shell) =>
        {
            shell.ShowTab("chaster");
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<ChasterTabView>("ChasterTab")!;
            // The sheet is local; raffle cutoff is UTC, including when local midnight is ahead.
            var utc = DateTime.UtcNow.Date;
            var first = utc.AddDays(-2);
            view.BuildCalendar((first, utc.AddDays(1)), utc.AddDays(1));
            var calendar = view.FindControl<Panel>("Calendar")!;
            var cells = calendar.Children.Cast<Border>().ToArray();
            RaffleCard Card(params int[] days) => new(utc.ToString("yyyy-MM"), DateTime.DaysInMonth(utc.Year, utc.Month),
                utc.Day, days, 7200, 20, 36000, false, false, null);
            bool Dot(int i) => ((Panel)cells[i].Child!).Children.OfType<Ellipse>().Any(e => Equals(e.Tag, "raffle-day"));

            view.ShowRaffleCard(Card(utc.Day, utc.AddDays(1).Month == utc.Month ? utc.AddDays(1).Day : utc.Day));
            Assert.True(Dot(2));
            Assert.False(Dot(0));
            Assert.False(Dot(3));
            Assert.True(view.FindControl<Control>("CalendarRaffleKey")!.IsVisible);
            Assert.Equal(Loc.GetF("chaster_cal_served_raffle", 3), ToolTip.GetTip(cells[2]));
            view.ShowRaffleCard(Card());
            Assert.Same(cells[2], calendar.Children[2]);
            Assert.False(Dot(2));
            Assert.False(view.FindControl<Control>("CalendarRaffleKey")!.IsVisible);
            Assert.Equal(Loc.GetF("chaster_cal_served", 3), ToolTip.GetTip(cells[2]));

            view.ShowRaffleCard(Card(utc.Day));
            chaster.ForgetRaffle();
            Dispatcher.UIThread.RunJobs();
            Assert.False(Dot(2));
            Assert.Empty(view.FindControl<Panel>("RafflePips")!.Children);

            // A hidden page unsubscribes; its cached card still must not survive the return.
            view.ShowRaffleCard(Card(utc.Day));
            shell.ShowTab("settings");
            chaster.ForgetRaffle();
            shell.ShowTab("chaster");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(view.FindControl<Panel>("RafflePips")!.Children);
            return Task.CompletedTask;
        }));

    [Fact]
    public Task ConstructedServiceFollowsIdentityAndUnhooksOnDispose() => Run(() =>
    {
        var old = CoreAccount.UnifiedUserId;
        using var chaster = ChasterHead.Create("sandbox", null);
        try
        {
            var epoch = chaster.RaffleEpoch;
            CoreAccount.UnifiedUserId = "sync7-identity-test";
            Assert.Equal(epoch + 1, chaster.RaffleEpoch);
            chaster.Dispose();
            CoreAccount.UnifiedUserId = "sync7-identity-test-next";
            Assert.Equal(epoch + 1, chaster.RaffleEpoch);
        }
        finally { CoreAccount.UnifiedUserId = old; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, false)]
    public Task LiveArrivalRefreshesDotsWithoutLosingTheHold(double elapsed, bool immediate) => Run(() =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
        settings.Current.PerformanceMode = false;
        var clock = new Clock();
        BillboardCardHost.Time = clock;
        var provider = new Provider();
        var deck = new BillboardDeck(() => new[] { provider }, () => new(BillboardTier.Free, DateTime.UtcNow, DateTime.Now));
        var board = new BillboardCardHost(deck);
        var window = new Window { Width = 700, Height = 400, Content = board };
        try
        {
            window.Show();
            board.Begin();
            Dispatcher.UIThread.RunJobs();
            clock.Now += (long)(elapsed * TimeSpan.TicksPerSecond);
            board.HoldTick();
            provider.Live = true;
            board.MarkDirty();
            Assert.Equal(3, board.ChipButtons.Count);
            Assert.Equal(immediate ? "live" : "house.a", board.CurrentCard!.Spec.Id);
            if (!immediate)
            {
                Assert.InRange(board.HoldProgress, 0.49, 0.51);
                board.Advance();
                Assert.Equal("live", board.CurrentCard!.Spec.Id);
            }
            board.Advance();
            Assert.Equal("house.b", board.CurrentCard!.Spec.Id);
            window.Hide();
            provider.Live = false;
            board.MarkDirty();
            Assert.Equal(3, board.ChipButtons.Count);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, board.ChipButtons.Count);
            Assert.Equal("house.b", board.CurrentCard!.Spec.Id);
        }
        finally { window.Close(); BillboardCardHost.Time = TimeProvider.System; CoreSettings.ServiceProvider = oldSettings; }
        return Task.CompletedTask;
    });

    private static Task Run(Func<Task> action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.RenderProof.EnsureSetUp();
        return action();
    });

    private sealed class Clock : TimeProvider
    {
        internal long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private sealed class Provider : IBillboardProvider
    {
        internal bool Live;
        public string Id => "sync7";
        public event EventHandler? Changed { add { } remove { } }
        public void Invoke(string target) { }
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context)
        {
            if (Live) yield return Card("live", BillboardCardKind.Live, 0);
            yield return Card("house.a", BillboardCardKind.House, 0);
            yield return Card("house.b", BillboardCardKind.House, 1);
        }
        private static BillboardCardSpec Card(string id, BillboardCardKind kind, int priority) =>
            new(id, kind, priority, "", id, "", "#ff4fa8", null, null, BillboardAction.None);
    }
}
