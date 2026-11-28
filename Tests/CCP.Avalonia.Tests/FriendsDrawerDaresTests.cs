using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 583d5e464d, 67fadbf70f, e0065f6b76: the Dares strip in the drawer, the friend card in the
/// Dares look, the even watch switch and the row's right-click hint.</summary>
public sealed class FriendsDrawerDaresTests
{
    // b1: I lost and owe a Lockdown. o1: an offer to me.
    private const string Owed = """
        {"id":"b1","role":"to","type":"cant","state":"settled","deadline":"3h","friend":{"id":"f","name":"Fen"},
        "what":{"kind":"pics","target":100},"ends_at":"2026-11-18T13:00:00Z","at":"2026-11-18T09:00:00Z",
        "accepted_at":"2026-11-18T10:00:00Z","camera":false,"stakes":{"from":{"kind":"sp","amount":50},
        "to":{"kind":"lockdown","amount":10}},"dares":[],"winner":"from","counts":{"from":0,"to":0},"debt":{"state":"owed"}}
        """;
    private static readonly string Offer = Owed.Replace("\"settled\"", "\"sent\"").Replace("\"b1\"", "\"o1\"")
        .Replace("\"winner\":\"from\"", "\"winner\":null").Replace("\"debt\":{\"state\":\"owed\"}", "\"debt\":null");

    private sealed class Api : IDaresApi
    {
        internal readonly List<string> Ops = new();
        internal string Bets = Owed + "," + Offer;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Ops.Add(op);
            return Task.FromResult<JObject?>(JObject.Parse("{\"ok\":true,\"me\":{},\"bets\":[" + Bets + "]}"));
        }
    }

    private static T Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().First(c => c.Tag as string == tag);
    private static bool Has(Control root, string tag) => root.GetLogicalDescendants().OfType<Control>().Any(c => c.Tag as string == tag);
    private static void Press(Control root, string tag) => Tagged<Button>(root, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task Strip_lists_the_bets_and_its_cards_close_on_panic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        PanicSurfacesTests.EnsureApp();
        Assert.Null(DareHead.Service);
        var api = new Api();
        var host = new DarePanicTests.Host();
        using var dares = new DaresService(api, host, new DareMeter(), new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore()));
        var saved = (DareDebtCard.RunsAtStartup, DareIntro.Seen, DareIntro.MarkSeen);
        var (svc, _) = FriendsDrawerTests.Service();
        try
        {
            DareDebtCard.RunsAtStartup = () => false;
            (DareIntro.Seen, DareIntro.MarkSeen) = (() => true, () => { });
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            Assert.False(d.DaresStrip.IsVisible);   // dark: no strip
            DareHead.Service = dares;
            await dares.RefreshAsync();
            d.Render();
            Assert.True(d.DaresStrip.IsVisible);
            Assert.Equal(new[] { "o1", "b1" }, d.DareRowIds);   // WPF StripOrder: the offer leads, then what I owe

            Press(d, "dares-answer");
            Assert.NotNull(DareOfferCard.Open);
            Press(d, "dares-pay");
            Assert.NotNull(DareDebtCard.Open);
            Assert.DoesNotContain("start", api.Ops);   // a Lockdown stake waits for the card's consent
            Assert.False(host.Active);

            PanicSurfaces.All.Single(s => s.Id == "dares-cards").Stop(null!);
            Assert.Null(DareOfferCard.Open);
            Assert.Null(DareDebtCard.Open);
        }
        finally
        {
            DareDebtCard.CloseForPanic();
            DareOfferCard.CloseForPanic();
            DareHead.Service = null;
            DareHead.Resume();
            (DareDebtCard.RunsAtStartup, DareIntro.Seen, DareIntro.MarkSeen) = saved;
            svc.Dispose();
        }
    });

    [Fact]
    public Task Pay_in_the_strip_opens_the_debt_card_beside_the_main_window_not_over_it() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        PanicSurfacesTests.EnsureApp();
        var host = new DarePanicTests.Host();
        using var dares = new DaresService(new Api(), host, new DareMeter(), new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore()));
        var saved = (DareDebtCard.RunsAtStartup, DareIntro.Seen, DareIntro.MarkSeen);
        var (svc, _) = FriendsDrawerTests.Service();
        var main = new Window { Width = 400, Height = 600 };
        try
        {
            DareDebtCard.RunsAtStartup = () => false;
            (DareIntro.Seen, DareIntro.MarkSeen) = (() => true, () => { });
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc) { OwnerWindow = () => main };
            main.Show();
            DareHead.Service = dares;
            await dares.RefreshAsync();
            d.Render();

            Press(d, "dares-pay");   // WPF FriendsDrawer.Dares: DareDebtCard.Present, never ShowDialog
            var card = Assert.IsType<DareDebtCard>(DareDebtCard.Open);
            Assert.Same(main, card.Owner);
            Assert.False((bool)typeof(Window).GetField("_showingAsDialog",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(card)!);
            Assert.True(main.IsEnabled);

            PanicSurfaces.All.Single(s => s.Id == "dares-cards").Stop(null!);
            Assert.Null(DareDebtCard.Open);
            Assert.False(card.IsVisible);
        }
        finally
        {
            DareDebtCard.CloseForPanic();
            DareHead.Service = null;
            DareHead.Resume();
            (DareDebtCard.RunsAtStartup, DareIntro.Seen, DareIntro.MarkSeen) = saved;
            main.Close();
            svc.Dispose();
        }
    });

    [Fact]
    public Task Card_wears_the_dares_look_and_the_row_says_what_a_right_click_holds() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = FriendsDrawerTests.Service();
        try
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            d.OpenPickerFor("u_on", "poke");
            var card = Tagged<Control>(d, "friends-card");
            // WPF 67fadbf70f: More is the fourth tile in the grid, not a pill under it, and the picker sits in a labelled tray.
            var more = Tagged<Button>(card, "friends-action:more");
            Assert.IsType<global::Avalonia.Controls.Primitives.UniformGrid>(more.Parent);
            Assert.True(Has(card, "friends-tray:poke"));
            Assert.Contains(Tagged<Control>(card, "friends-tray:poke").GetLogicalDescendants().OfType<TextBlock>(),
                t => t.Text == Loc.Get("friends_action_poke").ToUpper(System.Globalization.CultureInfo.CurrentCulture));
            // WPF e0065f6b76: the row's header carries the hint, the open card never does.
            var header = Tagged<Grid>(Tagged<Border>(d, "friends-row:u_on"), "friends-row-head");
            Assert.Equal(Loc.Get("friends_row_tip"), ToolTip.GetTip(header));
            Assert.Null(ToolTip.GetTip(card));
        }
        finally { svc.Dispose(); }
    });

    [Fact]
    public Task Watch_switch_words_share_one_size() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var (svc, _) = FriendsDrawerTests.Service();
        var host = new Window { Width = 300, Height = 700 };
        try
        {
            LocalizationManager.Instance.SetLanguage("de");   // "Geschmack" is the long one (WPF e0065f6b76)
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            host.Content = d;
            host.Show();
            d.OpenPickerFor("u_on", "watch");
            for (int i = 0; i < 3; i++) host.UpdateLayout();
            var boxes = Tagged<Control>(d, "friends-watch-switch").GetLogicalDescendants().OfType<Viewbox>().ToList();
            Assert.Equal(3, boxes.Count);
            var words = boxes.Select(v => (TextBlock)v.Child!).ToList();
            Assert.Single(words.Select(t => t.FontSize).Distinct());
            // No cell shrinks its word on its own: each fits its box at the shared size.
            foreach (var v in boxes) Assert.True(v.Child!.DesiredSize.Width <= v.Bounds.Width + 0.5, ((TextBlock)v.Child).Text);
        }
        finally { host.Close(); LocalizationManager.Instance.SetLanguage(oldLanguage); svc.Dispose(); }
    });

    // WPF FriendsDrawer.Dares.cs:135 (the friend's photo), :157-158 (settled share), :267-268 (running share and hint).
    [Fact]
    public Task Rows_show_my_camera_share_and_the_friends_photo() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        PanicSurfacesTests.EnsureApp();
        var running = Owed.Replace("\"b1\"", "\"r1\"").Replace("\"settled\"", "\"running\"").Replace("\"camera\":false", "\"camera\":true,\"camera_seen\":{\"from\":90,\"to\":41}")
            .Replace("\"winner\":\"from\"", "\"winner\":null").Replace("\"debt\":{\"state\":\"owed\"}", "\"debt\":null")
            .Replace("\"name\":\"Fen\"", "\"name\":\"Fen\",\"avatar\":\"/av/fen.png\"").Replace("2026-11-18T13:00:00Z", "2099-01-01T00:00:00Z");
        var settled = Owed.Replace("\"camera\":false", "\"camera\":true,\"camera_seen\":{\"from\":10,\"to\":72}");
        var api = new Api { Bets = running + "," + settled };
        var host = new DarePanicTests.Host();
        using var dares = new DaresService(api, host, new DareMeter(), new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore()));
        var (svc, _) = FriendsDrawerTests.Service();
        var oldFetch = global::ConditioningControlPanel.Avalonia.Views.Overlays.KnockCard.FetchPicture;
        var fetched = new List<string>();
        try
        {
            global::ConditioningControlPanel.Avalonia.Views.Overlays.KnockCard.FetchPicture = url => { fetched.Add(url); return Task.FromException<byte[]>(new InvalidOperationException("no network in tests")); };
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            DareHead.Service = dares;
            await dares.RefreshAsync();
            d.Render();

            var run = Tagged<Border>(d, "dares-row:r1");
            var below = Tagged<Control>(run, "dares-camera-seen:below");
            var words = below.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(Loc.GetF("dares_cam_seen", 41, 60), words);
            Assert.Contains(Loc.Get(ConditioningControlPanel.Controls.Dares.DareCardRules.CameraHintKey), words);
            Assert.Contains(ConditioningControlPanel.Services.ProviderSubscription.ProxyBaseUrl + "/av/fen.png", fetched);

            var done = Tagged<Border>(d, "dares-row:b1");
            var ok = Tagged<Control>(done, "dares-camera-seen:ok");
            words = ok.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal(new[] { Loc.GetF("dares_cam_seen_final", 72, 60) }, words);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.Views.Overlays.KnockCard.FetchPicture = oldFetch;
            DareHead.Service = null;
            DareHead.Resume();
            svc.Dispose();
        }
    });
}
