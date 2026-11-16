using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Services.UI;
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

    [Fact]
    public void LookConsumesItsQueuedOfferAndEscapeAdvancesToTheOtherOffer() =>
        LeashGateCardTests.Run(false, (shell, _, service) =>
        {
            service.ApplyBlock(Block((First, "juno"), (Second, "vex"))); // Both windows are hidden.
            shell.Show();
            shell.ShowTab("leash");
            var page = shell.Named<LeashTabView>("LeashTab")!;
            try
            {
                var row = page.GetLogicalDescendants().OfType<Border>().Single(c => c.Tag as string == "leash-offer-row:vex");
                var look = row.GetLogicalDescendants().OfType<Button>().Single(c => c.Tag as string == "leash-offer-look");
                LeashGateCardTests.Click(look);
                var manual = Assert.IsType<LeashAskWindow>(LeashAskWindow.Current);
                Assert.Equal("leash-ask:vex", manual.Card.Tag);
                manual.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Dispatcher.UIThread.RunJobs();
                var next = Assert.IsType<LeashAskWindow>(LeashAskWindow.Current);
                Assert.Equal("leash-ask:juno", next.Card.Tag); // The other offer still waits.
                next.Card.Dismiss();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(LeashAskWindow.Current); // The offer opened by Look must not return.
                Assert.Contains(page.GetLogicalDescendants().OfType<Border>(), c => c.Tag as string == "leash-offer-row:vex");
            }
            finally
            {
                service.ApplyBlock(Block());
                LeashAskWindow.CloseCurrent();
                Dispatcher.UIThread.RunJobs();
            }
        });

    [Theory]
    [InlineData("wayland-test", null)]
    [InlineData(null, "wayland")]
    public void WaylandWithoutPortalShowsTheAvailableCutButtonOnTheOwnCard(string? display, string? session) =>
        LeashGateCardTests.Run(true, (shell, _, service) =>
        {
            var old = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var oldSession = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            try
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", display);
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", session);
                Assert.False(PortalPanicShortcut.IsBound);
                shell.Show();
                shell.ShowTab("leash");
                var page = shell.Named<LeashTabView>("LeashTab")!;
                var hint = page.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Tag as string == "leash-self-hold");
                var expected = OperatingSystem.IsLinux() ? Loc.Get("leash_self_cut_wayland")
                    : Loc.GetF("leash_self_hold_hint", LeashHoldToCut.KeyLabel(CoreSettings.Current.PanicKey));
                Assert.Equal(expected, hint.Text);
                Assert.NotEqual("leash_self_cut_wayland", hint.Text);
                var cut = page.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag as string == "leash-cut");
                LeashGateCardTests.Click(cut);
                Assert.Null(service.Snapshot.Me);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", old);
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", oldSession);
            }
        });

    [Theory]
    [InlineData("wayland-test", null)]
    [InlineData(null, "wayland")]
    public void WaylandWithoutPortalExplainsTheAvailableCutRouteBeforeAccepting(string? display, string? session) =>
        LeashGateCardTests.Run(false, (_, _, _) =>
        {
            var old = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var oldSession = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            try
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", display);
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", session);
                Assert.False(PortalPanicShortcut.IsBound);
                var card = new LeashAskCard();
                var line = card.GetLogicalDescendants().OfType<Grid>().Single(c => c.Tag as string == "leash-explain-line:cut");
                var text = line.Children.OfType<TextBlock>().Single().Text;
                var expected = OperatingSystem.IsLinux() ? Loc.GetF("leash_explain_cut_wayland", Loc.Get("nav_tab_leash"))
                    : Loc.GetF("leash_explain_leashed_cut", LeashHoldToCut.KeyLabel(CoreSettings.Current.PanicKey));
                Assert.Equal(expected, text);
                Assert.NotEqual("leash_explain_cut_wayland", text);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", old);
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", oldSession);
            }
        });

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("es")]
    [InlineData("pt-BR")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("de")]
    [InlineData("ru")]
    public void WaylandAskNamesTheActualNavigationTabInEveryLanguage(string language) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            var loc = LocalizationManager.Instance;
            var oldLanguage = loc.CurrentLanguage;
            var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            try
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-test");
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", null);
                loc.SetLanguage(language);
                var tab = NavSections.Find(NavSections.Social)!.Tabs.Single(t => t.Key == "leash");
                Assert.Equal("nav_tab_leash", tab.LabelKey);
                var label = Loc.Get(tab.LabelKey);
                Assert.NotEqual(tab.LabelKey, label);
                Assert.Contains("{0}", Loc.Get("leash_explain_cut_wayland"));
                var waylandText = Loc.GetF("leash_explain_cut_wayland", label);
                Assert.Contains(label, waylandText);
                var card = new LeashAskCard();
                var line = card.GetLogicalDescendants().OfType<Grid>().Single(c => c.Tag as string == "leash-explain-line:cut");
                var expected = OperatingSystem.IsLinux() ? waylandText
                    : Loc.GetF("leash_explain_leashed_cut", LeashHoldToCut.KeyLabel(CoreSettings.Current.PanicKey));
                Assert.Equal(expected, line.Children.OfType<TextBlock>().Single().Text);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", display);
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", session);
                loc.SetLanguage(oldLanguage);
            }
        });

    [Theory]
    [InlineData("Escape", "Esc")]
    [InlineData("F10", "F10")]
    public void OwnCardShowsTheConfiguredHoldToCutKey(string key, string label) =>
        LeashGateCardTests.Run(true, (shell, _, _) =>
        {
            var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            try
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", null);
                CoreSettings.Current.PanicKey = key;
                shell.ShowTab("leash");
                var page = shell.Named<LeashTabView>("LeashTab")!;
                var hint = page.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Tag as string == "leash-self-hold");
                Assert.Equal(Loc.GetF("leash_self_hold_hint", label), hint.Text);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", display);
                Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", session);
            }
        });
}
