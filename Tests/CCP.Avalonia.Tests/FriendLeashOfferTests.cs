using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendLeashOfferTests
{
    private sealed class Wire : ILeashApi
    {
        internal int Calls;
        internal bool Fail;
        internal TaskCompletionSource<JObject?>? Hold;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls++;
            if (Hold != null) return Hold.Task;
            return Task.FromResult<JObject?>(Fail ? null : new JObject { ["ok"] = true, ["status"] = "sent", ["offer_id"] = "0123456789abcdef" });
        }
    }
    private static Button Button(Control root, string tag) => root.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == tag);
    private static void Click(Control root, string tag) => Button(root, tag).RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
    private static Friend Friend(bool online = true) => new("friend", "Mia", null, 0, online, FriendPresence.None, false);
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        CoreSettings.Current.LeashIntroSeenHolder = false;
    }
    [Fact]
    public void FirstOfferRequiresConfirmationAndRemembersIt() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var wire = new Wire();
        var service = new LeashService(wire, () => "me");
        var sent = new HashSet<string>();
        var card = new FriendLeashOffer(Friend(), service, sent);
        Click(card, "leash-offer");
        Assert.Equal(0, wire.Calls);
        Click(card.Intro!, "leash-explain-not-now");
        Assert.False(CoreSettings.Current.LeashIntroSeenHolder);
        Assert.Equal(0, wire.Calls);
        Click(card, "leash-offer");
        Click(card.Intro!, "leash-explain-offer");
        Assert.True(CoreSettings.Current.LeashIntroSeenHolder);
        Assert.Equal(1, wire.Calls);
        Assert.Contains("friend", sent);
        var another = new FriendLeashOffer(Friend() with { Id = "other" }, service, sent);
        Click(another, "leash-offer");
        Assert.Equal(2, wire.Calls);
    });
    [Fact]
    public void UnavailableAndFailedOffersDoNotClaimSuccess() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        LeashHead.Service = null;
        Assert.Null(FriendLeashOffer.Create(Friend(), new HashSet<string>(), new HashSet<string>(), () => { }));
        var wire = new Wire { Fail = true };
        var service = new LeashService(wire, () => "me");
        var sent = new HashSet<string>();
        var stale = new FriendLeashOffer(Friend(false), service, sent);
        Assert.False(Button(stale, "leash-offer").IsEnabled);
        CoreSettings.Current.LeashIntroSeenHolder = true;
        var card = new FriendLeashOffer(Friend(), service, sent);
        Click(card, "leash-offer");
        Assert.Empty(sent);
        Assert.False(Button(card, "leash-offer").IsEnabled);
        Assert.Equal(Loc.Get("leash_res_failed"), ((TextBlock)Button(card, "leash-offer").Content!).Text);
    });
    [Fact]
    public void SentReceiptAndHoldingStatesAreShown() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var service = new LeashService(new Wire(), () => "me");
        var sent = new HashSet<string> { "friend" };
        service.OfferAsync("friend").GetAwaiter().GetResult();
        service.ApplyReceipts(new[] { new LeashReceipt("0123456789abcdef", LeashItemKind.Offer, "friend", "Mia", LeashStep.Seen, DateTimeOffset.UtcNow) });
        var offered = new FriendLeashOffer(Friend(), service, sent);
        Assert.Contains(Loc.Get("leash_step_seen"), ((TextBlock)Button(offered, "leash-offer").Content!).Text);
        Assert.False(Button(offered, "leash-offer").IsEnabled);
        var held = new JObject
        {
            ["who"] = new JObject { ["id"] = "friend", ["name"] = "Mia" }, ["online"] = true,
            ["intensity"] = "soft", ["since"] = DateTimeOffset.UtcNow.ToString("o"), ["day"] = 1,
            ["week"] = new JArray(), ["pending"] = new JArray(), ["punished_today"] = 0,
        };
        service.ApplyBlock(new JObject { ["holding"] = new JArray { held }, ["offers"] = new JArray(), ["events"] = new JArray() });
        var holding = new FriendLeashOffer(Friend(), service, sent);
        Assert.Equal(Loc.Get("leash_offer_holding"), ((TextBlock)Button(holding, "leash-offer").Content!).Text);
        Assert.False(Button(holding, "leash-offer").IsEnabled);
    });

    [Fact]
    public Task FriendCardHostsTheOffer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var (friends, _) = FriendsDrawerTests.Service();
        await friends.RefreshAsync();
        LeashHead.Service = new LeashService(new Wire(), () => "me");
        var drawer = new FriendsDrawer(friends);
        drawer.Toggle("u_on");
        Click(drawer, "leash-offer");
        Assert.NotNull(drawer.GetLogicalDescendants().OfType<FriendLeashOffer>().Single().Intro);
        drawer.GetLogicalDescendants().OfType<FriendLeashOffer>().Single().Intro!.Close();
    });

    [Fact]
    public Task RepaintCannotSendTheSameOfferTwice() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        CoreSettings.Current.LeashIntroSeenHolder = true;
        var wire = new Wire { Hold = new TaskCompletionSource<JObject?>() };
        var service = new LeashService(wire, () => "me");
        var sent = new HashSet<string>();
        var pending = new HashSet<string>();
        var first = new FriendLeashOffer(Friend(), service, sent, pending);
        Click(first, "leash-offer");
        var repainted = new FriendLeashOffer(Friend(), service, sent, pending);
        Assert.False(Button(repainted, "leash-offer").IsEnabled);
        Assert.Equal(1, wire.Calls);
        wire.Hold.SetResult(new JObject { ["ok"] = true, ["status"] = "sent" });
        await FriendsDrawerTests.Until(() => sent.Contains("friend"));
        Assert.Contains("friend", sent);
    });

    [Fact]
    public Task DrawersShareOffersAndAnswersPermitAnotherOffer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup(); CoreSettings.Current.UnifiedId = "account-a"; CoreSettings.Current.LeashIntroSeenHolder = true;
        var (friends, _) = FriendsDrawerTests.Service(); await friends.RefreshAsync();
        var wire = new Wire(); var service = new LeashService(wire, () => "me"); LeashHead.Service = service;
        var first = new FriendsDrawer(friends); var second = new FriendsDrawer(friends, asPage: true);
        var window = new Window { Content = new StackPanel { Children = { first, second } } };
        window.Show(); first.Toggle("u_on"); second.Toggle("u_on");
        try
        {
            Click(first, "leash-offer");
            Assert.Equal(1, wire.Calls);
            Assert.False(Button(second, "leash-offer").IsEnabled);
            void Answer(string id, bool accepted) => service.ApplyBlock(new JObject
            {
                ["holding"] = new JArray(), ["offers"] = new JArray(), ["events"] = new JArray(new JObject
                { ["id"] = id, ["kind"] = "answered", ["from"] = new JObject { ["id"] = "u_on", ["name"] = "Mia" }, ["at"] = DateTimeOffset.UtcNow.ToString("o"), ["accepted"] = accepted })
            });
            Answer("declined", false);
            Assert.True(Button(second, "leash-offer").IsEnabled);
            Click(second, "leash-offer"); Assert.Equal(2, wire.Calls);
            Answer("accepted", true);
            Assert.True(Button(first, "leash-offer").IsEnabled);
            Click(first, "leash-offer"); Assert.Equal(3, wire.Calls);
            var held = new JObject { ["who"] = new JObject { ["id"] = "u_on", ["name"] = "Mia" }, ["online"] = true, ["intensity"] = "soft", ["since"] = DateTimeOffset.UtcNow.ToString("o"), ["day"] = 1, ["week"] = new JArray(), ["pending"] = new JArray() };
            service.ApplyBlock(new JObject { ["holding"] = new JArray { held }, ["events"] = new JArray() });
            Assert.False(Button(first, "leash-offer").IsEnabled);
            service.ApplyBlock(new JObject { ["holding"] = new JArray(), ["events"] = new JArray() });
            Assert.True(Button(second, "leash-offer").IsEnabled);
            Click(second, "leash-offer"); Assert.Equal(4, wire.Calls);
            CoreSettings.Current.UnifiedId = "account-b";
            LeashHead.CheckOfferAccount();
            Assert.True(Button(first, "leash-offer").IsEnabled);
        }
        finally { window.Close(); LeashHead.Service = null; }
    });

    [Fact]
    public void HelpShowsTheSameIllustratedExplanationWithoutSending() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); var wire = new Wire(); var card = new FriendLeashOffer(Friend(), new LeashService(wire, () => "me"), new HashSet<string>());
        Click(card, "leash-offer");
        var intro = card.Intro!;
        Assert.Equal(Loc.GetF("leash_explain_offer_title", "Mia"), intro.Title);
        Assert.Equal(4, intro.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Path>().Count(p => ((string?)p.Tag)?.StartsWith("leash-explain-art:") == true));
        Click(intro, "leash-explain-not-now"); Assert.Equal(0, wire.Calls); Assert.False(CoreSettings.Current.LeashIntroSeenHolder);
        Click(card, "leash-offer-help"); intro = card.Intro!;
        Assert.Equal(Loc.Get("leash_explain_title"), intro.Title);
        Assert.Null(intro.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => (string?)b.Tag == "leash-explain-offer"));
        Click(intro, "leash-explain-not-now"); Assert.Equal(0, wire.Calls);
    });

}
