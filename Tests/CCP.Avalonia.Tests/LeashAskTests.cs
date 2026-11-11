using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LeashDrawerSection offer row -> LeashSurfaces.ShowAsk -> LeashAskCard, from the Leash page.</summary>
public sealed class LeashAskTests
{
    private const string OfferId = "0123456789abcdef";

    private sealed class Wire : ILeashApi
    {
        public readonly List<(string Op, JObject Body)> Calls = new();
        public JObject Reply = new() { ["ok"] = true, ["status"] = "on" };
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, (JObject)body.DeepClone()));
            return Task.FromResult<JObject?>(op == "answer" ? Reply : new JObject { ["ok"] = true });
        }
    }

    private static void Run(Action<MainShellWindow, LeashTabView, Wire, List<string>> body) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            LeashGateCardTests.Run(false, (shell, _, _) =>
            {
                var wire = new Wire();
                var seen = new List<string>();
                var svc = new LeashService(wire, () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
                var at = DateTimeOffset.UtcNow.AddMinutes(-5);
                svc.ApplyBlock(new JObject
                {
                    ["me"] = null, ["holding"] = new JArray(), ["events"] = new JArray(),
                    ["offers"] = new JArray { new JObject
                    {
                        ["id"] = OfferId, ["from"] = new JObject { ["id"] = "u_juno", ["name"] = "Juno", ["avatar"] = null },
                        ["at"] = at.ToString("o"), ["expires_at"] = at.AddDays(7).ToString("o"),
                    } },
                });
                LeashHead.Service = svc;
                CoreSettings.Current.LeashIntroSeenLeashed = false;
                try
                {
                    shell.Show(); shell.ShowTab("leash"); Dispatcher.UIThread.RunJobs();
                    body(shell, shell.Named<LeashTabView>("LeashTab")!, wire, seen);
                }
                finally { LeashAskWindow.CloseCurrent(); }
            });
        });

    private static T Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Single(c => c.Tag as string == tag);

    [Fact]
    public void OfferRowLookOpensTheAskWhichWaitsForTheReadClockThenPutsItOn() =>
        Run((shell, page, wire, seen) =>
        {
            Assert.False(page.ShowingEmpty);
            Tagged<Border>(page, "leash-offer-row:u_juno");
            Assert.Equal(new[] { OfferId }, seen); // The row on screen is the offer seen.
            LeashLevelHelp.Open = false;
            CoreSettings.Current.PanicKey = "F10";

            LeashGateCardTests.Click(Tagged<Button>(page, "leash-offer-look"));
            var ask = LeashAskWindow.Current!;
            Assert.True(ask.IsVisible);
            var card = ask.Card;
            Assert.Equal(new[] { OfferId }, seen); // Seen once, however many surfaces show it.
            Assert.Equal(Loc.GetF("leash_explain_leashed_cut", "F10"), Tagged<Grid>(card, "leash-explain-line:cut").Children.OfType<TextBlock>().Single().Text);
            Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Standard)), Tagged<TextBlock>(card, "leash-guide-tagline").Text);
            Assert.False(Tagged<ScrollViewer>(card, "leash-guide-table").IsVisible);
            LeashGateCardTests.Click(Tagged<Button>(card, "leash-guide-toggle"));
            Assert.True(Tagged<ScrollViewer>(card, "leash-guide-table").IsVisible);
            Assert.Equal(220, Tagged<ScrollViewer>(card, "leash-guide-table").MaxHeight);
            Assert.Equal(Loc.GetF(LeashLevelGuide.LimitsKey, LeashLevelGuide.PunishmentsPerDay, LeashLevelGuide.ExpiresAfterHours), Tagged<TextBlock>(card, "leash-guide-limits").Text);
            Assert.False(card.PutItOnButton.IsEnabled); // First ask: read the explainer first.
            Assert.Equal(4, card.GetLogicalDescendants().OfType<Border>().Count(b => (b.Tag as string)?.StartsWith("leash-explain-panel:") == true));
            card.IsVisible = false;
            card.ReadTick(TimeSpan.FromSeconds(10));
            Assert.False(card.PutItOnButton.IsEnabled);
            card.IsVisible = true;
            Assert.Equal(Loc.Get("leash_explain_read_first"), ToolTip.GetTip(card.PutItOnButton));
            card.ReadTick(TimeSpan.FromMilliseconds(LeashIntroRule.AskReadMs - 1000)); // margin for a real 100 ms tick under load
            Assert.False(card.PutItOnButton.IsEnabled);
            card.ReadTick(TimeSpan.FromMilliseconds(1000));
            Assert.True(card.PutItOnButton.IsEnabled);

            LeashGateCardTests.Click(Tagged<Button>(card, "leash-ask-level:2"));
            Assert.Equal(LeashIntensity.Strict, card.Level);
            Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Strict)), Tagged<TextBlock>(card, "leash-guide-tagline").Text);
            Assert.True(Tagged<ScrollViewer>(card, "leash-guide-table").IsVisible);
            Assert.Contains(card.GetLogicalDescendants().OfType<Border>(), b => b.Tag as string == "leash-allow:chaster:on");
            LeashGateCardTests.Click(card.PutItOnButton);
            var (op, sent) = wire.Calls.Last();
            Assert.Equal("answer", op);
            Assert.Equal("u_juno", (string?)sent["from"]);
            Assert.True((bool?)sent["accept"]);
            Assert.Equal("strict", (string?)sent["intensity"]);
            Assert.Null(LeashAskWindow.Current);
            Assert.False(ask.IsVisible);
            Assert.True(CoreSettings.Current.LeashIntroSeenLeashed);
        });

    [Fact]
    public void AnAnswerThatDidNotGoThroughKeepsTheCardOpenAndSaysWhy() =>
        Run((shell, page, wire, _) =>
        {
            CoreSettings.Current.LeashIntroSeenLeashed = true;
            LeashGateCardTests.Click(Tagged<Button>(page, "leash-offer-look"));
            var card = LeashAskWindow.Current!.Card;
            Assert.True(card.PutItOnButton.IsEnabled); // Seen before: no read clock.

            wire.Reply = new JObject { ["ok"] = false };
            LeashGateCardTests.Click(card.PutItOnButton);
            Assert.Equal(Loc.Get("leash_ask_err_failed"), card.ErrorShown);
            Assert.NotNull(LeashAskWindow.Current);
            wire.Reply = new JObject { ["ok"] = false, ["reason"] = "off" };
            LeashGateCardTests.Click(card.PutItOnButton);
            Assert.Equal(Loc.Get("leash_ask_err_off"), card.ErrorShown);
            Assert.NotNull(LeashAskWindow.Current);
            Assert.True(card.PutItOnButton.IsEnabled);

            wire.Reply = new JObject { ["ok"] = true, ["status"] = "gone" };
            LeashGateCardTests.Click(Tagged<Button>(card, "leash-ask-no"));
            Assert.Equal(Loc.GetF("leash_ask_err_gone", "Juno"), card.ErrorShown);
            Assert.False(card.PutItOnButton.IsEnabled);
            Assert.NotNull(LeashAskWindow.Current);
            LeashGateCardTests.Click(Tagged<Button>(card, "leash-ask-no")); // Gone: "Not now" is a plain close.
            Assert.Null(LeashAskWindow.Current);
            Assert.Equal(3, wire.Calls.Count(c => c.Op == "answer"));
        });

    [Fact]
    public void EscapeClosesTheAskAndTheOfferStays() =>
        Run((shell, page, wire, _) =>
        {
            LeashGateCardTests.Click(Tagged<Button>(page, "leash-offer-look"));
            var previous = LeashAskWindow.Current!;
            LeashGateCardTests.Click(Tagged<Button>(page, "leash-offer-look"));
            var ask = LeashAskWindow.Current!;
            Assert.False(previous.IsVisible);
            Assert.NotSame(previous, ask);
            ask.Card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.Null(LeashAskWindow.Current);
            Assert.DoesNotContain(wire.Calls, c => c.Op == "answer");
            Tagged<Border>(page, "leash-offer-row:u_juno");
        });
    [Fact]
    public void NotNowDeclinesWithoutWaitingForTheReadClock() =>
        Run((_, page, wire, _) =>
        {
            wire.Reply = new JObject { ["ok"] = true, ["status"] = "declined" };
            LeashGateCardTests.Click(Tagged<Button>(page, "leash-offer-look"));
            var card = LeashAskWindow.Current!.Card;
            Assert.False(card.PutItOnButton.IsEnabled);
            LeashGateCardTests.Click(Tagged<Button>(card, "leash-ask-no"));
            Assert.False((bool?)wire.Calls.Last().Body["accept"]);
            Assert.Null(LeashAskWindow.Current);
            Assert.True(CoreSettings.Current.LeashIntroSeenLeashed);
        });

}
