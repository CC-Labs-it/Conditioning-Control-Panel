using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeashIncomingAskTests
{
    private const string First = "0123456789abcdef", Second = "fedcba9876543210";
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-28T12:00:00Z");

    private static JObject Block(params (string Id, string From)[] offers) => new()
    {
        ["me"] = null, ["holding"] = new JArray(), ["events"] = new JArray(),
        ["offers"] = new JArray(offers.Select(o => new JObject
        {
            ["id"] = o.Id, ["from"] = new JObject { ["id"] = o.From, ["name"] = o.From },
            ["at"] = At.ToString("o"), ["expires_at"] = At.AddDays(7).ToString("o"),
        })),
    };

    [Fact]
    public void IncomingOffersWaitForAVisibleOwnerThenShowOnceInOrderAndReportSeen() =>
        LeashGateCardTests.Run(false, (shell, launcher, _) =>
        {
            var seen = new List<string>();
            var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", reportSeen: seen.Add);
            LeashHead.Service = svc;
            shell.RebindLeashAsks();
            try
            {
                svc.ApplyBlock(Block((First, "juno"), (Second, "vex")));
                Assert.Null(LeashAskWindow.Current); // Both windows are hidden.
                Assert.Empty(seen);
                launcher.Show();
                shell.RebindLeashAsks(); // The same two-second retry as the gate timer.
                var first = Assert.IsType<LeashAskWindow>(LeashAskWindow.Current);
                Assert.Same(launcher, first.Owner);
                Assert.Equal("leash-ask:juno", first.Card.Tag);
                Assert.Equal(new[] { First }, seen);
                svc.ApplyBlock(Block((First, "juno"), (Second, "vex"))); // A poll must not duplicate the queue.
                first.Card.Dismiss();
                Dispatcher.UIThread.RunJobs();
                var second = Assert.IsType<LeashAskWindow>(LeashAskWindow.Current);
                Assert.Equal("leash-ask:vex", second.Card.Tag);
                Assert.Equal(new[] { First, Second }, seen);
                second.Card.Dismiss();
                Dispatcher.UIThread.RunJobs();
                shell.RebindLeashAsks();
                Assert.Null(LeashAskWindow.Current);
            }
            finally
            {
                LeashHead.Service = null;
                shell.RebindLeashAsks();
                LeashAskWindow.CloseCurrent();
                Dispatcher.UIThread.RunJobs();
            }
        });

    [Fact]
    public void WithdrawnOffersAndOldServiceCannotOpenAfterRebind() =>
        LeashGateCardTests.Run(false, (shell, _, oldService) =>
        {
            oldService.ApplyBlock(Block((First, "juno"), (Second, "vex")));
            oldService.ApplyBlock(Block((Second, "vex"))); // Juno withdrew while hidden.
            shell.Show();
            shell.RebindLeashAsks();
            try
            {
                Assert.Equal("leash-ask:vex", LeashAskWindow.Current!.Card.Tag);
                var replacement = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
                LeashHead.Service = replacement;
                shell.RebindLeashAsks();
                LeashAskWindow.Current!.Card.Dismiss();
                Dispatcher.UIThread.RunJobs();
                oldService.ApplyBlock(Block((First, "juno")));
                Assert.Null(LeashAskWindow.Current);
                replacement.ApplyBlock(Block((First, "juno")));
                Assert.Equal("leash-ask:juno", LeashAskWindow.Current!.Card.Tag);
                Assert.Same(shell, LeashAskWindow.Current.Owner);
            }
            finally
            {
                LeashHead.Service = null;
                shell.RebindLeashAsks();
                LeashAskWindow.CloseCurrent();
                Dispatcher.UIThread.RunJobs();
            }
        });

    [Fact]
    public void GateTimerPicksUpAReplacementServiceAndItsWaitingOffer() =>
        LeashGateCardTests.Run(false, (shell, _, _) =>
        {
            var replacement = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
            replacement.ApplyBlock(Block((First, "juno")));
            LeashHead.Service = replacement;
            shell.Show();
            try
            {
                Assert.Null(LeashAskWindow.Current);
                // The WPF gate timer also rebinds the surfaces every two seconds.
                System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(2200));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("leash-ask:juno", LeashAskWindow.Current!.Card.Tag);
            }
            finally
            {
                LeashHead.Service = null;
                shell.RebindLeashAsks();
                LeashAskWindow.CloseCurrent();
                Dispatcher.UIThread.RunJobs();
            }
        });

    [Theory]
    [InlineData("Escape", "Esc")]
    [InlineData("F10", "F10")]
    public void OwnCardShowsTheConfiguredHoldToCutKey(string key, string label) =>
        LeashGateCardTests.Run(true, (shell, _, _) =>
        {
            CoreSettings.Current.PanicKey = key;
            shell.ShowTab("leash");
            var page = shell.Named<LeashTabView>("LeashTab")!;
            var hint = page.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Tag as string == "leash-self-hold");
            Assert.Equal(Loc.GetF("leash_self_hold_hint", label), hint.Text);
        });
}
