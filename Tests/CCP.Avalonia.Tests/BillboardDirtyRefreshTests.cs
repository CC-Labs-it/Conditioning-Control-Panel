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

public sealed class BillboardDirtyRefreshTests
{
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
