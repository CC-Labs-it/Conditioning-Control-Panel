using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Models;
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
        internal string Status = "sent";
        internal TaskCompletionSource<JObject?>? Hold;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls++;
            if (Hold != null) return Hold.Task;
            return Task.FromResult<JObject?>(Fail ? null : new JObject { ["ok"] = true, ["status"] = Status, ["offer_id"] = "0123456789abcdef" });
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
    [Theory]
    [InlineData("sent", LeashCue.Sent)]
    [InlineData("too_fast", LeashCue.Denied)]
    [InlineData("dnd", LeashCue.Denied)]
    [InlineData("unknown", LeashCue.Denied)]
    public void OfferPlaysCueAndShowsLink(string status, LeashCue expected) => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var previous = settings.LeashIntroSeenHolder;
        try
        {
            Setup(); settings.LeashIntroSeenHolder = true;
            var card = new FriendLeashOffer(Friend(), new LeashService(new Wire { Status = status }, () => "me"), new HashSet<string>());
            var played = new List<LeashCue>(); card.PlayCue = played.Add;
            Assert.Contains(Button(card, "leash-offer").GetLogicalDescendants().OfType<ConditioningControlPanel.Avalonia.Controls.IconGlyph>(), x => x.Kind == FluentIcons.Common.Symbol.Link);
            Click(card, "leash-offer");
            Assert.Equal(expected, Assert.Single(played));
        }
        finally { settings.LeashIntroSeenHolder = previous; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfferCueTestRestoresIntroSetting(bool previous)
    {
        var settings = CoreSettings.Current;
        var original = settings.LeashIntroSeenHolder;
        try
        {
            settings.LeashIntroSeenHolder = previous;
            OfferPlaysCueAndShowsLink("sent", LeashCue.Sent);
            Assert.Equal(previous, settings.LeashIntroSeenHolder);
        }
        finally { settings.LeashIntroSeenHolder = original; }
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
        Assert.Equal(Loc.Get("leash_res_failed"), ((StackPanel)Button(card, "leash-offer").Content!).Children.OfType<TextBlock>().Single().Text);
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
        Assert.Contains(Loc.Get("leash_step_seen"), ((StackPanel)Button(offered, "leash-offer").Content!).Children.OfType<TextBlock>().Single().Text);
        Assert.False(Button(offered, "leash-offer").IsEnabled);
        var held = new JObject
        {
            ["who"] = new JObject { ["id"] = "friend", ["name"] = "Mia" }, ["online"] = true,
            ["intensity"] = "soft", ["since"] = DateTimeOffset.UtcNow.ToString("o"), ["day"] = 1,
            ["week"] = new JArray(), ["pending"] = new JArray(), ["punished_today"] = 0,
        };
        service.ApplyBlock(new JObject { ["holding"] = new JArray { held }, ["offers"] = new JArray(), ["events"] = new JArray() });
        var holding = new FriendLeashOffer(Friend(), service, sent);
        Assert.Equal(Loc.Get("leash_offer_holding"), ((StackPanel)Button(holding, "leash-offer").Content!).Children.OfType<TextBlock>().Single().Text);
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

    [Fact]
    public void EscapeCancelsFirstOfferWithoutConsent() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); var wire = new Wire(); var card = new FriendLeashOffer(Friend(), new LeashService(wire, () => "me"), new HashSet<string>());
        Click(card, "leash-offer"); var intro = card.Intro!;
        var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        intro.RaiseEvent(key);
        Assert.True(key.Handled); Assert.Null(card.Intro); Assert.False(CoreSettings.Current.LeashIntroSeenHolder); Assert.Equal(0, wire.Calls);
    });

    [Fact]
    public void IntroMotionUsesPreferenceAndAnimatesTheActualParts() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); var settings = CoreSettings.Current; var prior = settings.MotionLevel;
        try
        {
            settings.MotionLevel = MotionLevel.Off;
            var still = new LeashHolderIntro("Mia", null); still.Show();
            Assert.False(still.MotionActive);
            Assert.All(still.GetLogicalDescendants().OfType<Border>().Where(b => ((string?)b.Tag)?.StartsWith("leash-explain-panel:") == true), p => Assert.Equal(1, p.Opacity));
            still.Close();
            settings.MotionLevel = MotionLevel.Reduced;
            var reduced = new LeashHolderIntro("Mia", null); reduced.Show(); Assert.True(reduced.MotionActive);
            reduced.MotionFrame(1100); Assert.All(reduced.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>(), bit => Assert.False(bit.IsVisible)); reduced.Close();
            settings.MotionLevel = MotionLevel.Full;
            var moving = new LeashHolderIntro("Mia", null); moving.Show(); Assert.True(moving.MotionActive);
            var parts = moving.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Path>().ToArray();
            T Transform<T>(string tag) where T : Transform => (T)parts.Single(p => (string?)p.Tag == tag).RenderTransform!;
            var panels = moving.GetLogicalDescendants().OfType<Border>().Where(b => ((string?)b.Tag)?.StartsWith("leash-explain-panel:") == true).ToArray();
            moving.MotionFrame(80); Assert.True(panels[0].Opacity > panels[3].Opacity);
            moving.MotionFrame(260); Assert.Equal(0.2, Transform<ScaleTransform>("leash-bar:0").ScaleY, 3);
            Assert.Equal(new RelativePoint(0.5, 53.0 / 70, RelativeUnit.Relative), parts.Single(p => (string?)p.Tag == "leash-bar:0").RenderTransformOrigin);
            moving.MotionFrame(630); Assert.Equal(0.1, Transform<ScaleTransform>("leash-eye").ScaleY, 3);
            moving.MotionFrame(920); Assert.Equal(-18, Transform<RotateTransform>("leash-blade-a").Angle, 3); Assert.Equal(18, Transform<RotateTransform>("leash-blade-b").Angle, 3);
            Assert.Equal(5, Transform<TranslateTransform>("leash-explain-art:4").X, 3); Assert.Equal(-5, Transform<TranslateTransform>("leash-cut-right").X, 3);
            moving.MotionFrame(1100); Assert.True(Transform<TranslateTransform>("leash-cut-right").X > -5);
            Assert.All(moving.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>(), bit => Assert.Equal(ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowParticles, bit.IsVisible));
            moving.MotionFrame(2000); Assert.Equal(1, Transform<ScaleTransform>("leash-bar:0").ScaleY, 3); Assert.Equal(1, Transform<ScaleTransform>("leash-eye").ScaleY, 3); Assert.Equal(0, Transform<RotateTransform>("leash-blade-a").Angle, 3);
            moving.Close(); Assert.False(moving.MotionActive);
        }
        finally { settings.MotionLevel = prior; }
    });

    [Fact]
    public void IntroDragStartsOnlyOnLeftPress() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); var settings = CoreSettings.Current; var old = settings.MotionLevel; settings.MotionLevel = MotionLevel.Off;
        var calls = 0; var w = new LeashHolderIntro("Mia", null) { DragOverride = _ => calls++ };
        try
        {
            w.Show(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var title = w.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == Loc.Get("leash_explain_title"));
            var point = title.TranslatePoint(new Point(5, 5), w)!.Value;
            w.MouseDown(point, MouseButton.Right); w.MouseUp(point, MouseButton.Right); Assert.Equal(0, calls);
            w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Assert.Equal(1, calls);
        }
        finally { w.Close(); settings.MotionLevel = old; }
    });

}
