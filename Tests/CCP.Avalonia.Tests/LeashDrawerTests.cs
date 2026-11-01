using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF FriendsDrawer.cs:394-395 and LeashDrawerSection.cs:37-40, :94-150.</summary>
public sealed class LeashDrawerTests
{
    private const string FirstId = "0123456789abcdef", SecondId = "fedcba9876543210", ThirdId = "1122334455667788", Aid = "abcdef0123456789";

    private static JObject Offer(string id, string name)
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        return new JObject
        {
            ["id"] = id, ["from"] = new JObject { ["id"] = "u_" + name, ["name"] = name },
            ["at"] = at.ToString("o"), ["expires_at"] = at.AddDays(7).ToString("o"),
        };
    }

    private static T Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Single(c => c.Tag as string == tag);

    private static void Run(bool punished, Action<MainShellWindow, FriendsRailChip, LeashService> body) =>
        AvaloniaTestDispatcher.Run(() => LeashGateCardTests.Run(punished, (shell, _, service) =>
        {
            var oldFriends = FriendsHead.Service;
            var (friends, _) = FriendsDrawerTests.Service();
            FriendsHead.Service = friends;
            try
            {
                shell.Show();
                shell.ShowTab("friends");
                Dispatcher.UIThread.RunJobs();
                var chip = shell.Named<FriendsRailChip>("FriendsChip")!;
                body(shell, chip, service);
            }
            finally
            {
                LeashAskWindow.CloseCurrent();
                FriendsHead.Service = oldFriends;
            }
        }));

    [Fact]
    public void RailOfferIsPinnedAboveFriendsAndLookOpensOverTheShell() => Run(false, (shell, chip, _) =>
    {
        var seen = new List<string>();
        var service = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
        service.ApplyBlock(new JObject { ["offers"] = new JArray(Offer(FirstId, "Juno")) });
        LeashHead.Service = service;
        Assert.Empty(seen);
        chip.Toggle();
        Dispatcher.UIThread.RunJobs();
        Assert.True(chip.IsOpen);
        var section = Tagged<LeashTabView>(chip.Drawer, "leash-section");
        Assert.Same(section, Assert.IsType<StackPanel>(section.Parent).Children[0]);
        Tagged<Border>(section, "leash-offer-row:u_Juno");
        Assert.DoesNotContain(section.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-page-open-friends");
        Assert.Equal(new[] { FirstId }, seen);
        var page = shell.Named<FriendsTabView>("FriendsTab")!;
        Assert.DoesNotContain(page.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-section");
        LeashGateCardTests.Click(Tagged<Button>(section, "leash-offer-look"));
        Assert.NotNull(LeashAskWindow.Current);
        Assert.Contains(LeashAskWindow.Current!, shell.OwnedWindows);
        Assert.Equal(new[] { FirstId }, seen);
    });

    [Fact]
    public void PopupUpdatesWhileOpenStopsWhileClosedAndRebindsOnReopen() => Run(false, (_, chip, _) =>
    {
        var seen = new List<string>();
        var service = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", cutSafety: () => { }, reportSeen: seen.Add);
        var block = LeashHeadTests.BlockWithPunishment();
        block["me"]!["assignment"] = new JObject
        {
            ["aid"] = Aid, ["kind"] = "minutes", ["size"] = 15, ["day"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["status"] = "open", ["at"] = DateTimeOffset.UtcNow.ToString("o"),
        };
        block["offers"] = new JArray(Offer(FirstId, "Juno"));
        service.ApplyBlock(block);
        Assert.NotNull(service.Snapshot.Me!.Assignment);
        LeashHead.Service = service;
        chip.Toggle(); Dispatcher.UIThread.RunJobs();
        var section = Tagged<LeashTabView>(chip.Drawer, "leash-section");
        var self = Tagged<LeashSelfCard>(section, "leash-self-card");
        Assert.Equal(new[] { FirstId, Aid }, seen);
        block["offers"] = new JArray(Offer(SecondId, "Mia"));
        service.ApplyBlock(block);
        Assert.Same(self, Tagged<LeashSelfCard>(section, "leash-self-card"));
        Tagged<Border>(section, "leash-offer-row:u_Mia");
        Assert.Equal(new[] { FirstId, Aid, SecondId }, seen);
        chip.Drawer.Render(); // A friends repaint keeps the same section and card.
        Assert.Same(section, Tagged<LeashTabView>(chip.Drawer, "leash-section"));
        Assert.Same(self, Tagged<LeashSelfCard>(section, "leash-self-card"));
        chip.Toggle(); Dispatcher.UIThread.RunJobs();
        service.ApplyBlock(new JObject { ["offers"] = new JArray(Offer(ThirdId, "Zed")) });
        Tagged<Border>(section, "leash-offer-row:u_Mia"); // No hidden repaint.
        Assert.Equal(new[] { FirstId, Aid, SecondId }, seen);
        var replacement = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", cutSafety: () => { });
        replacement.ApplyBlock(new JObject { ["offers"] = new JArray(Offer(FirstId, "Nia")) });
        LeashHead.Service = replacement;
        chip.Toggle(); Dispatcher.UIThread.RunJobs();
        Tagged<Border>(section, "leash-offer-row:u_Nia");
        Assert.DoesNotContain(section.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-self-card");
    });

    [Fact]
    public void PopupCutUsesTheShellPathAndClosesTheLeashGate() => Run(true, (shell, chip, service) =>
    {
        chip.Toggle(); Dispatcher.UIThread.RunJobs();
        var section = Tagged<LeashTabView>(chip.Drawer, "leash-section");
        shell.PresentLeashGate();
        Assert.True(shell.LeashGate!.IsUp);
        LeashGateCardTests.Click(Tagged<Button>(section, "leash-cut"));
        Assert.Null(service.Snapshot.Me);
        Assert.False(shell.LeashGate.IsUp);
        Assert.DoesNotContain(section.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-self-card");
    });
}
