using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ConditioningControlPanel;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;
using IconGlyph = ConditioningControlPanel.Avalonia.Controls.IconGlyph;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>The friends rail chip and drawer over the real Core FriendsService, its wire faked in
/// process (never a server), and the service's timer seam.</summary>
public sealed partial class FriendsDrawerTests
{
    /// <summary>WPF FriendsSfx on the card: an action button clicks (FriendsDrawer.cs:714), a sent poke pops and a
    /// refused one is denied (ShowResult, :1144), through the landing's Sfx (same files, levels and tags).</summary>
    [Theory]
    [InlineData("click")]
    [InlineData("sent")]
    [InlineData("denied")]
    public Task TheCardsActionsAndSendResultsSoundLikeWpf(string cue) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsRailChip(svc).Drawer;
        await svc.RefreshAsync();
        d.Toggle("u_on");
        var cues = new List<string>();
        var saved = new CueState();
        saved.Quiet();
        ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => cues.Add(tag);
        ConditioningControlPanel.CoreSettings.Current.MasterVolume = 50;
        try
        {
            FriendsLanding.SfxGate = new();
            if (cue == "click") Click(d, "friends-action:poke");
            else
            {
                Assert.Equal(SendResult.Sent, await d.PokeAsync("u_on", PokeSet.Shipped[0]));
                if (cue == "denied")
                {
                    cues.Clear();
                    FriendsLanding.SfxGate = new();
                    Assert.Equal(SendResult.TooFast, await d.PokeAsync("u_on", PokeSet.Shipped[1]));
                }
            }
            Assert.Equal(new[] { "friends-" + cue }, cues);
        }
        finally { saved.Restore(); }
    });

    /// <summary>WPF FriendsDrawer.Platforms + :558: an online row wears one mark per platform, desktop / phone / web
    /// order whatever the wire said, each with its tooltip; an offline row wears none.</summary>
    [Fact]
    public Task AnOnlineRowWearsItsPlatformMarksWithTooltips() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        wire.StateBody = State.Replace("\"squelched\":false}", "\"squelched\":false,\"platforms\":[\"web\",\"phone\",\"desktop\"]}");
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        d.Render();
        var marks = Tagged<StackPanel>(Tagged<Border>(d, "friends-row:u_on")!, "friends-platforms")!;
        Assert.Equal(new[] { "friends-platform:desktop", "friends-platform:phone", "friends-platform:web" }, marks.Children.Select(c => c.Tag as string));
        Assert.Equal(new[] { IconKind.Desktop, IconKind.Phone, IconKind.Globe },
            marks.Children.Select(c => c.GetLogicalDescendants().OfType<IconGlyph>().Single().Kind));
        Assert.Equal(new object?[] { Loc.Get("friends_platform_desktop"), Loc.Get("friends_platform_phone"), Loc.Get("friends_platform_web") },
            marks.Children.Select(c => ToolTip.GetTip(c)));
        Assert.Null(Tagged<StackPanel>(Tagged<Border>(d, "friends-row:u_off")!, "friends-platforms"));
    });

    private const string State = """
        {"ok":true,"code":"CCP-ABCDE","me":{"activity":"panel","lock_day":null,"shared":false},
         "friends":[{"id":"u_on","name":"Mia","tier":0,"online":true,"activity":"session","lock_day":3,"last_seen":null,"squelched":false},
                    {"id":"u_off","name":"Zed","tier":0,"online":false,"activity":"online","lock_day":null,"last_seen":"2026-09-01T00:00:00Z","squelched":false}],
         "incoming":[{"id":"u_req","name":"Ann","via":null,"at":"2026-09-01T00:00:00Z"}],"outgoing":[],"blocked":[{"id":"u_bad","name":"Bob"}]}
        """;

    internal sealed class Wire : HttpMessageHandler
    {
        public readonly List<string> Ops = new();
        /// <summary>The bodies of every "send" (poke / invite / watch), in order.</summary>
        public readonly List<string> Sends = new();
        public readonly List<string> Reports = new();
        /// <summary>When set, a "send" waits for it: the answer is still out while the test folds the drawer.</summary>
        public TaskCompletionSource? HoldSends;
        public string StateBody = State;
        public bool ActSucceeds = true;
        public string? LinkBody, ClaimBody;
        public readonly List<string> Polls = new();
        public TaskCompletionSource? HoldLinks;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            lock (Ops) Ops.Add(op);
            if (op == "report") Reports.Add(await r.Content!.ReadAsStringAsync(ct));
            if (op == "send") { Sends.Add(await r.Content!.ReadAsStringAsync(ct)); if (HoldSends != null) await HoldSends.Task; }
            if (op == "poll") Polls.Add(await r.Content!.ReadAsStringAsync(ct));
            if (op is "link" or "claim" && HoldLinks != null) await HoldLinks.Task;
            var body = op == "link" ? LinkBody ?? "{}" : op == "claim" ? ClaimBody ?? """{"ok":true,"status":"friends"}""" : op == "state" ? StateBody : op == "poll" ? """{"ok":true,"online":[],"inbox":[],"receipts":[]}"""
                : op == "send" ? """{"ok":true,"status":"sent"}""" : ActSucceeds ? """{"ok":true}""" : """{"ok":false,"error":"try_later"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    /// <summary>Lets a click's async handler finish: dispatcher turns until done or a 10 s wall-clock cap.
    /// A turn count alone ran out on a loaded gate before the thread-pool send continuation posted back.</summary>
    internal static async Task Until(Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < end)
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
    }

    private static void Click(Control root, string tag) =>
        Tagged<Button>(root, tag)!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>WPF FriendsDrawer.BuildCard + Pickers: the card's three actions open their picker inline, and a
    /// poke chip, a flavour chip and the HT box each send through the service and word the answer in the row.</summary>
    [Fact]
    public Task TheCardsPickersSendAPokeAWatchAndAnInvite() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsRailChip(svc).Drawer;
        await svc.RefreshAsync();
        d.Toggle("u_on");
        foreach (var act in new[] { "invite", "poke", "watch" }) Assert.NotNull(Tagged<Button>(d, "friends-action:" + act));
        // WPF ActionButton's MDL2 glyphs (E8A7 / E7C9 / E714 / E712), restored as Fluent icons.
        IconKind glyph(string tag) => Tagged<Button>(d, tag)!.GetLogicalDescendants().OfType<IconGlyph>().Single().Kind;
        Assert.Equal(new[] { IconKind.Open, IconKind.HandPoint, IconKind.Video, IconKind.MoreHorizontal },
            new[] { "invite", "poke", "watch", "more" }.Select(a => glyph("friends-action:" + a)));
        Assert.Null(d.OpenPicker);

        Click(d, "friends-action:poke");
        Assert.Equal("poke", d.OpenPicker);
        Assert.Equal(PokeSet.Shipped.Count, Tagged<WrapPanel>(d, "friends-picker:poke")!.Children.Count);
        Click(d, "friends-poke:" + PokeSet.Shipped[0]);
        await Until(() => Tagged<TextBlock>(d, "friends-result") != null);
        Assert.Equal(Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.Sent)), Tagged<TextBlock>(d, "friends-result")!.Text);
        // A second poke inside the cooldown is refused by the service (PokeSet.CooldownSeconds) and never sent.
        Assert.Equal(SendResult.TooFast, await d.PokeAsync("u_on", PokeSet.Shipped[1]));
        Assert.Single(wire.Sends);
        d.Render();
        Assert.Contains("\"kind\":\"poke\"", wire.Sends[0]);
        Assert.Contains("\"poke\":\"" + PokeSet.Shipped[0] + "\"", wire.Sends[0]);
        Assert.Equal(Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.TooFast)), Tagged<TextBlock>(d, "friends-result")!.Text);

        Click(d, "friends-action:watch");
        Assert.Equal("watch", d.OpenPicker);
        Click(d, "friends-flavour:trance");
        await Until(() => wire.Sends.Count == 2 && Tagged<TextBlock>(d, "friends-result")?.Text == Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.Sent)));
        Click(d, "friends-watch-tab:ht");
        var box = Tagged<TextBox>(d, "friends-ht-box")!;
        Assert.False(Tagged<Button>(d, "friends-ht-send")!.IsEnabled);
        box.Text = "ht-1234!";
        Assert.Equal("1234", box.Text);   // digits only, as typed (FriendsDrawerRules.NormaliseHtId)
        // A pasted Hypnotube link gives the number before .html, not the slug's digits (WPF 43137acb4: no MaxLength cut).
        box.Text = "";
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "https://hypnotube.com/video/bimbo-2024-trance-12345.html" });
        Assert.Equal("12345", box.Text);
        box.Text = "1234";
        Assert.True(Tagged<Button>(d, "friends-ht-send")!.IsEnabled);
        Click(d, "friends-ht-send");
        Assert.Equal(SendResult.Sent, await d.SendWatchAsync("u_on", new WatchRef(WatchKind.Flavour, "pink", "Pink")));
        Assert.Contains(wire.Sends, s => s.Contains("\"kind\":\"flavour\"") && s.Contains("\"id\":\"trance\""));
        Assert.Contains(wire.Sends, s => s.Contains("\"kind\":\"ht\"") && s.Contains("\"id\":\"" + FriendsDrawerRules.NormaliseHtId("ht-1234!") + "\""));

        // Goon waits for its host; chess challenges use the board host.
        Click(d, "friends-action:invite");
        Assert.Equal("invite", d.OpenPicker);
        Assert.False(Tagged<Button>(d, "friends-invite:" + InviteDestination.Goon)!.IsEnabled);
        Assert.True(Tagged<Button>(d, "friends-invite:" + InviteDestination.Chess)!.IsEnabled);
        Click(d, "friends-invite:" + InviteDestination.BackRoom);
        Assert.Equal(SendResult.Sent, await d.InviteAsync("u_on", InviteDestination.Ramp, null));
        Assert.Contains(wire.Sends, s => s.Contains("\"kind\":\"invite\"") && s.Contains("\"destination\":\"backroom\""));

        // Pressing the lit action again folds its picker; folding the card forgets it.
        Click(d, "friends-action:invite");
        Assert.Null(d.OpenPicker);
        d.OpenPickerFor("u_on", "poke");
        d.Toggle("u_on");
        Assert.Null(d.OpenPicker);
    });

    [Fact]
    public Task ChessPickerWaitsForTheChallengeBeforeSendingItsInvite() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = FriendsInviteCodes.ChallengeFriend;
        var ready = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (svc, wire) = Service();
        try
        {
            FriendsInviteCodes.ChallengeFriend = (id, timeout) => { Assert.Equal("u_on", id); Assert.Equal(TimeSpan.FromSeconds(45), timeout); return ready.Task; };
            var d = new FriendsRailChip(svc).Drawer;
            await svc.RefreshAsync(); d.Toggle("u_on");
            Click(d, "friends-action:invite"); Click(d, "friends-invite:chess");
            Assert.Empty(wire.Sends);
            ready.SetResult("c_0123456789abcdef");
            await Until(() => wire.Sends.Count > 0);
            Assert.Contains("\"destination\":\"chess\"", wire.Sends.Single());
            Assert.Contains("c_0123456789abcdef", wire.Sends.Single());
        }
        finally { FriendsInviteCodes.ChallengeFriend = old; svc.Dispose(); }
    });

    /// <summary>WPF ShowResult: a send answered after the drawer folded says so outside, over the rail chip's
    /// window (the drawer itself sits in a popup root, then detached). Driven from the rail chip's click.</summary>
    [Fact]
    public Task ASendAnsweredAfterTheDrawerFoldsFliesOverTheChipsWindow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, wire) = Service();
        var chip = new FriendsRailChip(svc) { Width = 200, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom };
        var host = new Window { Width = 1000, Height = 700, Content = chip };
        try
        {
            host.Show();
            await svc.RefreshAsync();
            var at = chip.TranslatePoint(new Point(24, 24), host)!.Value;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(host, at, MouseButton.Left);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(host, at, MouseButton.Left);
            Assert.True(chip.IsOpen);
            var d = chip.Drawer;
            d.Toggle("u_on");
            Click(d, "friends-action:poke");
            wire.HoldSends = new TaskCompletionSource();
            Click(d, "friends-poke:" + PokeSet.Shipped[0]);
            await Until(() => wire.Sends.Count == 1);
            // Esc folds the picker, then the drawer (WPF OnKey).
            foreach (var _ in new[] { 1, 2 }) d.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(chip.IsOpen);
            wire.HoldSends.SetResult();
            await Until(() => host.OwnedWindows.OfType<FloatingWord>().Any());
            var word = Assert.Single(host.OwnedWindows.OfType<FloatingWord>());
            Assert.Equal(Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.Sent)), word.GetLogicalDescendants().OfType<TextBlock>().Single().Text);

            // Folded, the drawer no longer redraws on every snapshot (P07).
            var row = Tagged<Border>(d, "friends-row:u_on");
            wire.StateBody = State.Replace("\"Zed\"", "\"Zoe\"");
            await svc.RefreshAsync();
            Assert.Equal("Zoe", svc.Snapshot.Friends.Single(f => f.Id == "u_off").Name);
            Assert.Same(row, Tagged<Border>(d, "friends-row:u_on"));
        }
        finally { foreach (var w in host.OwnedWindows.ToArray()) w.Close(); host.Close(); }
    });

    /// <summary>Ticks only when the test says so; <see cref="Restarted"/> completes on the first Start after a tick.</summary>
    private sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public bool Running;
        public readonly TaskCompletionSource Restarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _ticked;
        public void Start() { Running = true; if (_ticked) Restarted.TrySetResult(); }
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() { _ticked = true; Tick?.Invoke(this, EventArgs.Empty); }
    }

    internal static (FriendsService Svc, Wire Wire) Service(string? account = "u_me")
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var wire = new Wire();
        var shared = false;
        var api = new FriendsApi(new HttpClient(wire), () => account == null ? null : (account, "tok"), "http://127.0.0.1:9");
        return (new FriendsService(api, () => account, () => true, null, () => shared, v => shared = v), wire);
    }
    private static T? Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    [Fact]
    public async Task TheCoreServicePollsOnTheHeadsTimer()
    {
        var (svc, wire) = Service();
        var timer = new FakeTimer();
        svc.Start(timer);
        Assert.True(timer.Running);
        Assert.Empty(wire.Ops);
        timer.Fire();
        await timer.Restarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "poll", "state" }, wire.Ops);
        // The state has Mia online and the app is in front: the fast cadence (FriendsPollRule).
        Assert.Equal(TimeSpan.FromSeconds(FriendsPollRule.FastSeconds), timer.Interval);
    }

    [Theory]
    [InlineData("/tmp/sandbox", null, null)]
    [InlineData("/tmp/sandbox", "https://codebambi-proxy.vercel.app", null)]
    [InlineData("/tmp/sandbox", "http://example.com:3001", null)]
    [InlineData("/tmp/sandbox", "http://127.0.0.1:3001/", "http://127.0.0.1:3001")]
    [InlineData(null, null, "https://codebambi-proxy.vercel.app")]
    public void ASandboxReachesOnlyALoopbackFriendsServer(string? userData, string? url, string? expected)
    {
        Assert.Equal(expected, FriendsHead.BaseUrl(userData, url));
        Assert.Equal(expected != null, FriendsHead.Create(userData, url) != null);
    }

    [Fact]
    public Task TheDrawerListsFriendsRequestsAndBlockedAndTheChipCountsOnline() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        wire.StateBody = State.Replace("\"outgoing\":[]", "\"outgoing\":[{\"id\":\"u_out\",\"name\":\"Oli\",\"via\":null,\"at\":\"2026-09-01T00:00:00Z\"}]");
        var chip = new FriendsRailChip(svc);
        await svc.RefreshAsync();
        Assert.Equal(1, chip.PillCount);
        var d = chip.Drawer;
        d.Render();
        foreach (var tag in new[] { "friends-row:u_on", "friends-row:u_off", "friends-request-in:u_req" }) Assert.NotNull(Tagged<Border>(d, tag));
        // WPF FriendsDrawer.cs:785-818: only an incoming request carries the Block menu
        Assert.NotNull(Tagged<Border>(d, "friends-request-in:u_req")!.ContextMenu);
        Assert.Null(Tagged<Border>(d, "friends-request-out:u_out")!.ContextMenu);
        Assert.Equal("CCP-ABCDE", Tagged<TextBlock>(d, "friends-my-code")!.Text);
        Assert.Null(Tagged<Border>(d, "friends-blocked:u_bad"));
        d.ToggleBlocked();
        Assert.NotNull(Tagged<Border>(d, "friends-blocked:u_bad"));
    });

    /// <summary>audit #1976: a click inside the drawer routes through the Popup to the chip; it must
    /// not toggle the drawer shut (WPF's HWND popup never reached the chip at all).</summary>
    [Fact]
    public Task AClickInsideTheDrawerKeepsItOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register)
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        await svc.RefreshAsync();
        var w = new Window { Width = 400, Height = 600, Content = new StackPanel { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom, Children = { chip } } };
        w.Show();
        try
        {
            Click(w, chip);
            Assert.True(chip.IsOpen);
            var code = Tagged<TextBlock>(chip.Drawer, "friends-my-code")!;
            Click(TopLevel.GetTopLevel(code)!, code);
            Assert.True(chip.IsOpen);
            Click(w, chip);
            Assert.False(chip.IsOpen);
        }
        finally { w.Close(); }
    });

    private static void Click(TopLevel top, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
        top.MouseDown(p, global::Avalonia.Input.MouseButton.Left);
        top.MouseUp(p, global::Avalonia.Input.MouseButton.Left);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task BlockAsksFirstAndAcceptAndUnblockGoToTheWire() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var mia = svc.Snapshot.Friends.First(f => f.Id == "u_on");
        await d.RunMenuAsync(mia, "block");
        Assert.DoesNotContain("block", wire.Ops);
        Assert.NotNull(Tagged<Border>(d, "friends-confirm:block"));
        Assert.Equal(ActResult.Done, await d.ConfirmAsync(mia.Id, mia.Name, "block"));
        Assert.Contains("block", wire.Ops);
        Assert.Equal(ActResult.Done, await d.AnswerRequestAsync(svc.Snapshot.Incoming[0], "accept"));
        Assert.Contains("accept", wire.Ops);
        Assert.Equal(ActResult.Done, await d.UnblockAsync(new BlockedFriend("u_bad", "Bob")));
        Assert.Contains("unblock", wire.Ops);
    });

    [Fact]
    public Task RemoveAsksFirstAndReportGoesToTheWire() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var zed = svc.Snapshot.Friends.First(f => f.Id == "u_off");
        await d.RunMenuAsync(zed, "remove");
        Assert.DoesNotContain("remove", wire.Ops);
        Assert.NotNull(Tagged<Border>(d, "friends-confirm:remove"));
        Assert.Equal(ActResult.Done, await d.ConfirmAsync(zed.Id, zed.Name, "remove"));
        Assert.Contains("remove", wire.Ops);
        Assert.Equal(ActResult.Done, await d.ReportAsync("u_on", "spam"));
        Assert.Contains("report", wire.Ops);
        Assert.Contains("u_on", Assert.Single(wire.Reports));
        Assert.DoesNotContain("in:u_on", wire.Reports[0]);
        Assert.Equal(Loc.Get("friends_report_done"), Tagged<TextBlock>(d, "friends-result")!.Text);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheFriendsPageAsksOnceAndRespectsBothAnswers(bool share) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var previousAsked = PresenceAsk.Asked;
        var previousMark = PresenceAsk.MarkAsked;
        bool asked = false;
        PresenceAsk.Asked = () => asked;
        PresenceAsk.MarkAsked = () => asked = true;
        var page = new global::ConditioningControlPanel.Avalonia.Views.Tabs.FriendsTabView(svc);
        try
        {
            await svc.RefreshAsync();
            page.OnShown();
            var ask = Tagged<Border>(page, "friends-presence-ask");
            Assert.NotNull(ask);
            Assert.True(ask.IsVisible);
            Assert.Contains(ask.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("friends_presence_ask"));
            Assert.Equal(1, Grid.GetRow(ask));
            Click(page, share ? "friends-presence-yes" : "friends-presence-no");
            Assert.True(asked);
            Assert.Equal(share, svc.PresenceShared);
            Assert.False(ask.IsVisible);
            page.OnHidden();
            page.OnShown();
            Assert.False(ask.IsVisible);  // switching away and back never repeats an answered question
            // Already sharing never asks, even when no marker exists yet.
            asked = false;
            svc.PresenceShared = true;
            page.Drawer.Render();
            Assert.False(ask.IsVisible);
            var signedOut = new global::ConditioningControlPanel.Avalonia.Views.Tabs.FriendsTabView(Service(null).Svc);
            Assert.False(Tagged<Border>(signedOut, "friends-presence-ask")!.IsVisible);
        }
        finally
        {
            page.OnHidden();
            PresenceAsk.Asked = previousAsked;
            PresenceAsk.MarkAsked = previousMark;
        }
    });

    [Fact]
    public Task ThePresenceSwitchFlipsSharingAndAnswersTheAsk() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var marked = 0;
        var before = PresenceAsk.MarkAsked;
        PresenceAsk.MarkAsked = () => marked++;
        try
        {
            var d = new FriendsDrawer(svc);
            await svc.RefreshAsync();
            d.TogglePresence();
            Assert.True(svc.PresenceShared);
            Assert.Equal(1, marked);
        }
        finally { PresenceAsk.MarkAsked = before; }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheOnceOnlyAskOffersBothAnswersAndStaysRetired(bool yes) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var oldAsked = PresenceAsk.Asked;
        var oldMark = PresenceAsk.MarkAsked;
        var asked = false;
        PresenceAsk.Asked = () => asked;
        PresenceAsk.MarkAsked = () => asked = true;
        try
        {
            var d = new FriendsDrawer(svc);
            await svc.RefreshAsync();
            Assert.NotNull(Tagged<Button>(d, "friends-presence-yes"));
            Assert.NotNull(Tagged<Button>(d, "friends-presence-no"));
            Click(d, yes ? "friends-presence-yes" : "friends-presence-no");
            Assert.Equal(yes, svc.PresenceShared);
            Assert.True(asked);
            Assert.Null(Tagged<Button>(d, "friends-presence-yes"));
            d.TogglePresence();
            d.TogglePresence();
            d.Render();
            Assert.Null(Tagged<Button>(d, "friends-presence-no"));
            var signedOut = new FriendsDrawer(Service(account: null).Svc);
            Assert.Null(Tagged<Button>(signedOut, "friends-presence-yes"));
            Assert.Equal(Loc.Get("friends_me_signed_out"), Tagged<TextBlock>(signedOut, "friends-me-status")!.Text);
        }
        finally { PresenceAsk.Asked = oldAsked; PresenceAsk.MarkAsked = oldMark; }
    });

    [Fact]
    public Task AnOldServerRemembersOnlySuccessfulBlocksAndUnblocksPerAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        wire.StateBody = State.Replace(",\"blocked\":[{\"id\":\"u_bad\",\"name\":\"Bob\"}]", "");
        var (old, oldAccount, oldHead) = (FriendsBlockList.Shared, FriendsBlockList.Account, FriendsHead.Service);
        var entries = new List<BlockedEntry>();
        FriendsBlockList.Shared = new FriendsBlockList(() => entries, rows => entries = rows.ToList());
        var account = "me";
        FriendsBlockList.Account = () => account;
        try
        {
            var d = new FriendsDrawer(svc);
            await svc.RefreshAsync();
            Assert.Null(svc.Snapshot.Blocked);
            wire.ActSucceeds = false;
            Assert.NotEqual(ActResult.Done, await d.ConfirmAsync("u_on", "Mia", "block"));
            Assert.Empty(entries);
            wire.ActSucceeds = true;
            Assert.Equal(ActResult.Done, await d.ConfirmAsync("u_on", "Mia", "block"));
            var remembered = Assert.Single(entries);
            d.ToggleBlocked();
            Assert.NotNull(Tagged<Border>(d, "friends-blocked:u_on"));
            account = "other";
            d.Render();
            Assert.Null(Tagged<Border>(d, "friends-blocked:u_on"));
            account = "me";
            wire.ActSucceeds = false;
            Assert.NotEqual(ActResult.Done, await d.UnblockAsync(remembered));
            Assert.Single(entries);
            wire.ActSucceeds = true;
            Assert.Equal(ActResult.Done, await d.UnblockAsync(remembered));
            Assert.Empty(entries);
            Assert.Null(Tagged<Border>(d, "friends-blocked:u_on"));
            Assert.Equal(ActResult.Done, await d.BlockRequesterAsync(svc.Snapshot.Incoming[0]));
            Assert.Equal("u_req", Assert.Single(entries).Id);
            Assert.NotNull(Tagged<Border>(d, "friends-blocked:u_req"));
            FriendsBlockList.Shared.Add("me", "stale", "Stale", DateTimeOffset.UtcNow);
            wire.StateBody = State;
            await svc.RefreshAsync();
            d.Render();
            Assert.NotNull(Tagged<Border>(d, "friends-blocked:u_bad"));
            Assert.Null(Tagged<Border>(d, "friends-blocked:stale"));
            Assert.Equal(2, entries.Count);   // WPF: showing the server's list never writes this PC's file
            FriendsHead.Service = svc;
            FriendsLanding.Tick();             // WPF FriendsLanding.cs:71: the landing tick retires it once
            Assert.Empty(entries);
            Assert.Equal(3, wire.Ops.Count(op => op == "block"));
        }
        finally
        {
            FriendsLanding.Stop();
            (FriendsBlockList.Shared, FriendsBlockList.Account, FriendsHead.Service) = (old, oldAccount, oldHead);
        }
    });

    [Fact]
    public Task SignedOutShowsTheSignInButtonAndNoCode() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var d = new FriendsDrawer(Service(account: null).Svc);
        Assert.NotNull(Tagged<Button>(d, "friends-sign-in"));
        Assert.Null(Tagged<TextBlock>(d, "friends-my-code"));
        return Task.CompletedTask;
    });

    /// <summary>WPF FriendsDrawer.Honesty TellOutside(always) -> FriendsLanding.Say -> FloatingWord.Throw:
    /// the removed row is gone, so the word flies over the window, owned and unfocusable, and closes itself.</summary>
    [Fact]
    public Task RemovingAFriendThrowsAFloatingWordOverTheWindowThatClosesItself() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, _) = Service();
        var d = new FriendsDrawer(svc);
        var host = new Window { Width = 1000, Height = 700, Content = d };
        try
        {
            host.Show();
            await svc.RefreshAsync();
            var zed = svc.Snapshot.Friends.First(f => f.Id == "u_off");
            Assert.Equal(ActResult.Done, await d.ConfirmAsync(zed.Id, zed.Name, "remove"));

            var word = Assert.Single(host.OwnedWindows.OfType<FloatingWord>());
            Assert.Equal(Loc.GetF("friends_removed_done", "Zed"), word.GetLogicalDescendants().OfType<TextBlock>().Single().Text);
            Assert.True(word.Topmost);
            Assert.False(word.ShowActivated);
            Assert.False(word.IsHitTestVisible);
            word.Step(800);
            Assert.Contains(word, host.OwnedWindows);
            word.Step(FloatingWord.TotalMs);
            Assert.True(word.IsDone);
            Assert.DoesNotContain(word, host.OwnedWindows);
        }
        finally { foreach (var w in host.OwnedWindows.ToArray()) w.Close(); host.Close(); }
    });

    /// <summary>WPF FloatingWord's keyframes at the stepped times they name (FloatingWord.cs:67-103).</summary>
    [Fact]
    public void TheFloatingWordPoseFollowsWpfsKeyframes()
    {
        const double th = 40;
        var start = FloatingWord.Pose(0, MotionLevel.Full, th);
        Assert.Equal((0.6, -6.0, 10.0, 0.0), (Round(start.Scale), Round(start.Angle), Round(start.Y), Round(start.Opacity)));
        var pop = FloatingWord.Pose(240, MotionLevel.Full, th);   // 15%
        Assert.Equal((1.15, 2.0, 0.0, 1.0), (Round(pop.Scale), Round(pop.Angle), Round(pop.Y), Round(pop.Opacity)));
        Assert.True(FloatingWord.Pose(100, MotionLevel.Full, th).Scale > 1.15 - 0.55 * 0.4167);   // BackEase overshoots linear
        Assert.Equal(1.0, FloatingWord.Pose(960, MotionLevel.Full, th).Opacity);                    // held to 60%
        var end = FloatingWord.Pose(1600, MotionLevel.Full, th);
        Assert.Equal((1.0, 0.0, -64.0, 0.0), (Round(end.Scale), Round(end.Angle), Round(end.Y), Round(end.Opacity)));
        Assert.Equal(-32.0, Round(FloatingWord.Pose(1600, MotionLevel.Reduced, th).Y));            // half the travel
        Assert.Equal((1.0, 0.0, 0.0, 1.0), FloatingWord.Pose(1100, MotionLevel.Off, th));            // still
        Assert.Equal(0.5, Round(FloatingWord.Pose(1300, MotionLevel.Off, th).Opacity));
        Assert.Equal((0.0, 0.0, 0.0), FloatingWord.Spark(180, 0, 100, 600));                         // waits 180 ms
        Assert.Equal(100.0, Round(FloatingWord.Spark(780 - 0.0001, 0, 100, 600).X));
        static double Round(double v) => Math.Round(v, 3) + 0.0;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheAddBoxOffersSubscribersTheInviteLink(bool subscriber) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsRailChip(svc).Drawer;
        await svc.RefreshAsync();
        d.OffersInviteLink = () => subscriber;
        d.Render();
        var asked = 0;
        d.InvitesRequested += () => asked++;
        Tagged<Button>(d, "friends-add-open")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(subscriber, Tagged<WrapPanel>(d, "friends-invite-line")!.IsVisible);
        Tagged<Button>(d, "friends-invite-link")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, asked);
    });
    // ---- open goon tables (WPF FriendsDrawer.Tables.cs; ConditioningControlPanel.Tests/FriendsDrawerTablesTests) ----

    private static global::ConditioningControlPanel.Services.GoonGame.OpenTablesReply ZedHosting() =>
        global::ConditioningControlPanel.Services.GoonGame.GoonOpenTablesApi.Parse(
            """{"ok":true,"tables":[{"code":"ZEDD42","name":"Zed","friend":true,"friendId":"u_off","cardSec":60}]}""");

    private static string[] RowIds(Control d) => d.GetLogicalDescendants().OfType<Border>()
        .Select(b => b.Tag as string).Where(t => t != null && t.StartsWith("friends-row:")).Select(t => t!.Substring(12)).ToArray();

    [Fact]
    public Task AHostingFriendFloatsToTheTopWithJoin() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        d.Render();
        Assert.Equal(new[] { "u_on", "u_off" }, RowIds(d));
        d.Tables = ZedHosting;
        d.CanJoinTables = () => true;
        d.Render();
        // Zed is offline, but the listing says they are at a table: they lead, pink, with Join.
        Assert.Equal(new[] { "u_off", "u_on" }, RowIds(d));
        var row = Tagged<Border>(d, "friends-row:u_off")!;
        Assert.NotNull(Tagged<TextBlock>(row, "friends-table-hosting"));
        Assert.Null(Tagged<TextBlock>(row, "friends-activity"));
        Assert.IsType<global::Avalonia.Media.LinearGradientBrush>(row.Background);
        Assert.Null(Tagged<Button>(Tagged<Border>(d, "friends-row:u_on")!, "friends-table-join"));
        // No Goon host on this head: Join words it in the row and the drawer stays open.
        int closed = 0;
        d.CloseRequested += () => closed++;
        Click(row, "friends-table-join");
        Assert.Equal(Loc.Get("exclusives_not_on_this_build"), Tagged<TextBlock>(d, "friends-result")!.Text);
        Assert.Equal(0, closed);
        // With a host, Join sits down with the table's code and folds the drawer (WPF PressJoin).
        var joined = new List<string>();
        d.JoinTable = joined.Add;
        Click(Tagged<Border>(d, "friends-row:u_off")!, "friends-table-join");
        Assert.Equal(new[] { "ZEDD42" }, joined);
        Assert.Equal(1, closed);
    });

    [Fact]
    public Task ASignedOutDrawerSeesALockThatNeverLaunches() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsDrawer(svc) { Tables = ZedHosting, CanJoinTables = () => false };
        var joined = new List<string>();
        d.JoinTable = joined.Add;
        await svc.RefreshAsync();
        d.Render();
        var row = Tagged<Border>(d, "friends-row:u_off")!;
        Assert.Null(Tagged<Button>(row, "friends-table-join"));
        Click(row, "friends-table-locked");
        Assert.Empty(joined);
        Assert.Null(Tagged<TextBlock>(d, "friends-result"));
    });

    [Fact]
    public Task TheTablesPollRunsOnlyWhileTheDrawerIsOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var tables = global::ConditioningControlPanel.Services.GoonGame.GoonOpenTables.Fetch;
        global::ConditioningControlPanel.Services.GoonGame.GoonOpenTables.ResetForTests();
        global::ConditioningControlPanel.Services.GoonGame.GoonOpenTables.Fetch = _ => Task.FromResult(ZedHosting());
        try
        {
            var (svc, _) = Service();
            var d = new FriendsDrawer(svc) { CanJoinTables = () => true };
            await svc.RefreshAsync();
            d.OnOpened();
            Assert.True(d.TablesPolling);
            Assert.Equal(FriendsDrawer.TablesPoll, TimeSpan.FromSeconds(30));
            // The listing lands through GoonOpenTables.Changed and repaints the open drawer.
            await Until(() => Tagged<TextBlock>(d, "friends-table-hosting") != null);
            Assert.NotNull(Tagged<Button>(d, "friends-table-join"));
            d.OnClosed();
            Assert.False(d.TablesPolling);
        }
        finally
        {
            global::ConditioningControlPanel.Services.GoonGame.GoonOpenTables.ResetForTests();
            global::ConditioningControlPanel.Services.GoonGame.GoonOpenTables.Fetch = tables;
        }
    });
    /// <summary>WPF App.xaml.cs:459: the open-tables listing rides the friends proxy and door; a sandbox
    /// reaches only a loopback address, else GoonOpenTablesApi has no address and asks nothing.</summary>
    [Fact]
    public void TheOpenTablesWireIsSeededWithTheFriendsDoor()
    {
        var w = global::ConditioningControlPanel.Services.Lobby.LobbyWire.DefaultIdentity;
        var u = global::ConditioningControlPanel.Services.Lobby.LobbyWire.DefaultBaseUrl;
        var (iw, iu) = (global::ConditioningControlPanel.Services.Invites.InviteApi.DefaultIdentity, global::ConditioningControlPanel.Services.Invites.InviteApi.DefaultBaseUrl);
        try
        {
            FriendsHead.SeedInvites("/tmp/sandbox", "http://127.0.0.1:3001/");
            Assert.Equal("http://127.0.0.1:3001", global::ConditioningControlPanel.Services.Lobby.LobbyWire.DefaultBaseUrl());
            FriendsHead.SeedInvites("/tmp/sandbox", null);
            Assert.Null(global::ConditioningControlPanel.Services.Lobby.LobbyWire.DefaultBaseUrl());
        }
        finally
        {
            (global::ConditioningControlPanel.Services.Lobby.LobbyWire.DefaultIdentity, global::ConditioningControlPanel.Services.Lobby.LobbyWire.DefaultBaseUrl) = (w, u);
            (global::ConditioningControlPanel.Services.Invites.InviteApi.DefaultIdentity, global::ConditioningControlPanel.Services.Invites.InviteApi.DefaultBaseUrl) = (iw, iu);
        }
    }

    /// <summary>WPF FriendsDrawer.Feed.cs + FriendsRailChip UpdateBadge: the chip's pink badge counts unread feed
    /// lines; opening the drawer shows "What happened" newest first, reads what it shows (badge drops, dots stay
    /// until the drawer folds), a line landing while open is drawn at once, folded it shows FoldMin with "more",
    /// and a line about a friend opens their card. Driven from the chip on a shown window.</summary>
    [Fact]
    public Task TheFeedShowsWhatHappenedReadsItAndTheChipBadgeCountsUnread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        ConditioningControlPanel.CoreSettings.Current.MotionLevel = MotionLevel.Full;
        ConditioningControlPanel.CoreSettings.Current.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var (svc, _) = Service();
        var feed = new FriendsFeed(() => "u_me");
        var chip = new FriendsRailChip(svc);
        chip.Drawer.FeedSource = () => feed;
        var t0 = DateTime.UtcNow.AddHours(-2);
        FriendEvent Ev(string key, string id, string name, int min) =>
            new(FriendEventKind.PokeReceived, id, name, t0.AddMinutes(min), key, Detail: PokeSet.Shipped[0]);
        var w = new Window { Width = 400, Height = 700, Content = new StackPanel { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom, Children = { chip } } };
        w.Show();
        try
        {
            await svc.RefreshAsync();
            Assert.True(feed.Add(Ev("a", "u_on", "Mia", 1)));
            Assert.True(feed.Add(Ev("b", "u_x", "Kit", 2)));
            Assert.True(feed.Add(Ev("c", "u_x", "Kit", 3)));
            Assert.Equal(3, chip.UnreadBadge);
            Assert.IsType<global::Avalonia.Media.ScaleTransform>(Tagged<Border>(chip, "friends-chip-unread")!.RenderTransform);

            chip.Toggle();
            Assert.True(chip.IsOpen);
            var d = chip.Drawer;
            Assert.True(d.FeedSection.IsVisible);
            Assert.Equal(new[] { "c", "b", "a" }, d.FeedLineKeys);
            Assert.Null(Tagged<Border>(d, "friends-feed-line:c")!.RenderTransform);
            Assert.Equal(0, feed.Unread);
            Assert.Equal(0, chip.UnreadBadge);
            Assert.Equal(3, d.GetLogicalDescendants().OfType<Control>().Count(c => c.Tag as string == "friends-feed-new"));
            Assert.Equal(Loc.GetF("friends_feed_new", 3), Tagged<TextBlock>(d, "friends-feed-count")!.Text);
            var line = Tagged<Border>(d, "friends-feed-line:a")!.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Tag as string == "friends-feed-text");
            Assert.Equal(FriendsDrawer.Body, line.FontFamily);   // WPF FriendsLook.Body
            Assert.Contains("Mia", string.Concat(line.Inlines!.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text)));

            // A line that lands while open is drawn at once, on top, and read.
            Assert.True(feed.Add(Ev("d", "u_x", "Kit", 4)));
            Assert.Equal("d", d.FeedLineKeys[0]);
            Assert.Equal(0, feed.Unread);
            Assert.IsType<global::Avalonia.Media.TranslateTransform>(Tagged<Border>(d, "friends-feed-line:d")!.RenderTransform);
            var dot = Assert.IsType<Border>(Tagged<Control>(d, "friends-feed-new"));
            Assert.Null(dot.Effect); // P82: an object initializer must not sneak an Effect into a row.
            Assert.Equal((6d, 6d, new CornerRadius(3)), (dot.Width, dot.Height, dot.CornerRadius));
            Assert.Same(FriendsDrawer.Pink, dot.Background);
            Assert.Equal(1, dot.BoxShadow.Count);
            var glow = dot.BoxShadow[0];
            Assert.Equal(6, glow.Blur);
            Assert.Equal(global::Avalonia.Media.Color.FromArgb(0xE6, 0xFF, 0x5F, 0xB4), glow.Color);
            Assert.Equal((0d, 0d, 0d), (glow.OffsetX, glow.OffsetY, glow.Spread));

            // WPF FriendsRailChip.cs:388: a dismissing press cannot reopen for 250 ms.
            chip.Toggle();
            chip.Toggle();
            Assert.False(chip.IsOpen);
            await Task.Delay(260);
            chip.Toggle();
            Assert.True(chip.IsOpen);
            // Reopened: nothing new, so FoldMin lines and "more" for the rest.
            Assert.Equal(FriendsFeedRules.FoldMin, d.FeedLineKeys.Count);
            Assert.Null(Tagged<TextBlock>(d, "friends-feed-count"));
            Assert.DoesNotContain(d.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "friends-feed-new");
            Assert.Null(Tagged<Button>(d, "friends-feed-less"));
            Assert.Same(FriendsDrawer.Hover, Tagged<Button>(d, "friends-feed-more")!.Resources["ButtonBackgroundPointerOver"]);   // WPF FeedLink hover
            Click(d, "friends-feed-more");
            Assert.Equal(4, d.FeedLineKeys.Count);
            Click(d, "friends-feed-less");
            Assert.Equal(FriendsFeedRules.FoldMin, d.FeedLineKeys.Count);

            // A line about someone on the list opens their card.
            Click(d, "friends-feed-more");
            d.OpenFromFeed("u_on");
            Assert.NotNull(Tagged<Button>(d, "friends-action:poke"));
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = MotionLevel.Off;
            Assert.True(feed.Add(Ev("e", "u_x", "Kit", 5)));
            Assert.Null(Tagged<Border>(d, "friends-feed-line:e")!.RenderTransform);
        }
        finally { chip.Toggle(); w.Close(); }
    });

    // Uses the normal feed subscriptions without Window.Show, which needs system fonts on Linux.
    [Fact]
    public Task LiveFeedVisualsFollowArrivalReadAndMotion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        ConditioningControlPanel.CoreSettings.Current.MotionLevel = MotionLevel.Full;
        ConditioningControlPanel.CoreSettings.Current.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var feed = new FriendsFeed(() => "visual-test");
        FriendsHead.Feed = feed;
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var d = chip.Drawer;
        FriendEvent Ev(string key) => new(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, key, Detail: PokeSet.Shipped[0]);
        try
        {
            Assert.Equal(1, Assert.IsType<global::Avalonia.Media.ScaleTransform>(Tagged<Border>(chip, "friends-chip-unread")!.RenderTransform).ScaleX); // #3657: resting ScaleTransform from construction
            feed.Add(Ev("first"));
            Assert.IsType<global::Avalonia.Media.ScaleTransform>(Tagged<Border>(chip, "friends-chip-unread")!.RenderTransform);
            d.OnOpened();
            Assert.Null(Tagged<Border>(d, "friends-feed-line:first")!.RenderTransform);
            Assert.True(feed.Add(Ev("second")));
            Assert.IsType<global::Avalonia.Media.TranslateTransform>(Tagged<Border>(d, "friends-feed-line:second")!.RenderTransform);
            var dot = Assert.IsType<Border>(Tagged<Control>(d, "friends-feed-new"));
            Assert.Null(dot.Effect); // P82: an object initializer must not sneak an Effect into a row.
            Assert.Equal((6d, 6d, new CornerRadius(3)), (dot.Width, dot.Height, dot.CornerRadius));
            Assert.Same(FriendsDrawer.Pink, dot.Background);
            Assert.Equal(1, dot.BoxShadow.Count);
            var glow = dot.BoxShadow[0];
            Assert.Equal(6, glow.Blur);
            Assert.Equal(global::Avalonia.Media.Color.FromArgb(0xE6, 0xFF, 0x5F, 0xB4), glow.Color);
            Assert.Equal((0d, 0d, 0d), (glow.OffsetX, glow.OffsetY, glow.Spread));
            Assert.Equal(0, feed.Unread);
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = MotionLevel.Off;
            feed.Add(Ev("still"));
            Assert.Null(Tagged<Border>(d, "friends-feed-line:still")!.RenderTransform);
        }
        finally { d.OnClosed(); }
        return Task.CompletedTask;
    });

    // WPF Feed.cs:334-344 and RailChip.cs:363-374: observe playback, not just its recipe.
    [Fact]
    public Task FeedAnimationsPlayToRestAndCancelOnDetach() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        ConditioningControlPanel.CoreSettings.Current.MotionLevel = MotionLevel.Full;
        ConditioningControlPanel.CoreSettings.Current.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var clock = new FeedStepClock();
        var rowClock = new FeedStepClock();
        void Step(double ms) { clock.Step(ms); rowClock.Step(ms); }
        var feed = new FriendsFeed(() => "playback-test");
        FriendsHead.Feed = feed;
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var chip = new FriendsRailChip(svc);
        var drawer = new FriendsDrawer(svc, asPage: true);
        drawer.SetValue(FeedStepClock.Property, rowClock.Instance);
        var host = new Canvas();
        var window = new Window { Width = 800, Height = 600, Content = host };
        window.SetValue(FeedStepClock.Property, clock.Instance);
        window.Show();
        // Attach before starting motion; the stepped clock owns animation time.
        host.Children.Add(chip);
        host.Children.Add(drawer);
        var badge = Tagged<Border>(chip, "friends-chip-unread")!;
        var now = DateTime.UtcNow;
        FriendEvent Ev(string key, int seconds) => new(FriendEventKind.PokeReceived, "u_on", "Mia", now.AddSeconds(seconds), key, Detail: PokeSet.Shipped[0]);
        double Scale() => badge.RenderTransform!.Value.M11;
        try
        {
            feed.Add(Ev("first", 0));
            Step(0); Step(60);
            Assert.Equal(1.225, Scale(), 4);
            Assert.Equal(Scale(), badge.RenderTransform!.Value.M22, 6);
            Step(120);
            Assert.Equal(1.45, Scale(), 4);
            Step(240);
            Assert.True(Scale() < 1); // elastic undershoot
            Step(420); Step(421);
            Assert.Equal(1, Scale(), 6);

            drawer.OnOpened();
            // Materialize the scroller without text measurement (Segoe UI may be absent on Linux).
            for (var pass = 0; pass < 3; pass++)
                foreach (var control in drawer.GetLogicalDescendants().OfType<global::Avalonia.Controls.Primitives.TemplatedControl>().ToArray())
                    control.ApplyTemplate();
            Assert.True(drawer.FeedSection.IsAttachedToVisualTree());
            feed.Add(Ev("second", 1));
            var row = Tagged<Border>(drawer, "friends-feed-line:second")!;
            Step(500); Step(570);
            Assert.InRange(row.RenderTransform!.Value.M31, -2.518, -2.516); // BackEase, t=0.25
            Assert.Equal(0.25, row.Opacity, 4); // fade stays linear
            Step(640);
            Assert.Equal(0.9, row.RenderTransform!.Value.M31, 4); // slight overshoot at t=0.5
            Assert.Equal(0.5, row.Opacity, 4);
            Step(780); Step(781);
            Assert.Equal(0, row.RenderTransform!.Value.M31, 6);
            Assert.Equal(1, row.Opacity, 6);
            Assert.False(rowClock.Watching);

            feed.Add(Ev("detach", 2));
            row = Tagged<Border>(drawer, "friends-feed-line:detach")!;
            Step(900); Step(970);
            Assert.InRange(row.Opacity, 0.24, 0.26);
            Assert.True(rowClock.Watching);
            host.Children.Remove(drawer);
            Step(980);
            Assert.False(rowClock.Watching); // stopped child clocks release their parent on the next pulse
            Assert.Equal(0, row.RenderTransform!.Value.M31, 6);
            Assert.Equal(1, row.Opacity, 6);
            drawer.OnClosed();

            feed.Add(Ev("badge-detach", 3));
            Step(1000); Step(1060);
            Assert.Equal(1.225, Scale(), 4);
            Assert.True(clock.Watching);
            host.Children.Remove(chip); // Unwire cancels the badge's in-flight run
            Step(1070);
            Assert.False(clock.Watching);
            Assert.Equal(1, Scale(), 6);
            Step(2000);
            Assert.Equal(1, Scale(), 6);
            Assert.Equal(0, row.RenderTransform!.Value.M31, 6);
            Assert.Equal(1, row.Opacity, 6);
        }
        finally { drawer.OnClosed(); window.Close(); }
    });

    // Same Avalonia 12 clock technique as SharedDeeperThemeTests: fail loudly on API drift.
    private sealed class FeedStepClock
    {
        private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        private static readonly Type Base = typeof(global::Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", true)!;
        private static readonly System.Reflection.MethodInfo Pulse = Base.GetMethod("Pulse", Any | System.Reflection.BindingFlags.Instance)!;
        public static readonly AvaloniaProperty Property = (AvaloniaProperty)typeof(global::Avalonia.Animation.Animatable)
            .GetField("ClockProperty", Any | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        private static readonly System.Reflection.PropertyInfo Subscriptions = Base.GetProperty("HasSubscriptions", Any | System.Reflection.BindingFlags.Instance)!;
        public bool Watching => (bool)Subscriptions.GetValue(Instance)!;
        public readonly object Instance = Activator.CreateInstance(Base, nonPublic: true)!;
        public void Step(double ms) => Pulse.Invoke(Instance, new object[] { TimeSpan.FromMilliseconds(ms) });
    }

    [Theory]
    [InlineData(MotionLevel.Off, 0)]
    [InlineData(MotionLevel.Reduced, 0.5)]
    [InlineData(MotionLevel.Full, 1)]
    public Task FeedMotionMatchesWpfTimingAndAmount(MotionLevel level, double amount) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ConditioningControlPanel.CoreSettings.Current.MotionLevel = level;
        ConditioningControlPanel.CoreSettings.Current.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var landing = FriendsDrawer.LandingAnimation();
        var bump = FriendsRailChip.BadgeAnimation();
        if (amount == 0) { Assert.Null(landing); Assert.Null(bump); return Task.CompletedTask; }
        Assert.NotNull(landing);
        Assert.NotNull(bump);
        Assert.Equal(TimeSpan.FromMilliseconds(280), landing.Duration);
        Assert.Equal(-12 * amount, Assert.IsType<global::Avalonia.Styling.Setter>(landing.Children[0].Setters[0]).Value);
        Assert.Equal(0.4 * amount, Assert.IsType<global::ConditioningControlPanel.Avalonia.Theme.BackEaseOutAmplitude>(landing.Easing).Amplitude);
        Assert.Equal(TimeSpan.FromMilliseconds(420), bump.Duration);
        Assert.Equal(1 + 0.45 * amount, Assert.IsType<global::Avalonia.Styling.Setter>(bump.Children[1].Setters[0]).Value);
        Assert.Equal(0, bump.Easing.Ease(0), 8);
        Assert.Equal(1, bump.Easing.Ease(120d / 420), 8);
        Assert.Equal(0, bump.Easing.Ease(1), 8);
        Assert.True(bump.Easing.Ease(240d / 420) < 0); // elastic return overshoots below rest
        return Task.CompletedTask;
    });

    /// <summary>WPF FriendsDrawer.Trail.cs: under a friend's name, the latest send's trail (kind, pips, state),
    /// repainted when the service says a trail moved.</summary>
    [Fact]
    public Task AFriendsRowShowsTheTrailOfWhatYouLastSentThem() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsRailChip(svc).Drawer;
        d.FeedSource = () => null;
        await svc.RefreshAsync();
        d.OnOpened();
        try
        {
            Assert.Null(d.TrailTextFor("u_off"));
            Assert.Equal(SendResult.Sent, await svc.PokeAsync("u_off", PokeSet.Shipped[0]));
            var t = svc.LastSentTo("u_off")!;
            var (kindKey, stateKey) = FriendsDrawerRules.TrailKeys(t);
            Assert.Equal(Loc.Get(kindKey) + "  " + "+.." + "  " + Loc.Get(stateKey), d.TrailTextFor("u_off"));
        }
        finally { d.OnClosed(); }
    });

    /// <summary>WPF FriendsDrawer.Honesty.cs TellOutside: an accept answered while the drawer is open cheers in
    /// the drawer only; answered after it folded, the word flies over the window instead.</summary>
    [Fact]
    public Task AnAcceptAnsweredAfterTheDrawerFoldedIsSaidOverTheWindow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, _) = Service();
        var d = new FriendsRailChip(svc).Drawer;
        d.FeedSource = () => null;
        var host = new Window { Width = 600, Height = 400 };
        d.OwnerWindow = () => host;
        host.Show();
        try
        {
            await svc.RefreshAsync();
            var req = svc.Snapshot.Incoming.Single();
            d.OnOpened();
            Assert.Equal(ActResult.Done, await d.AnswerRequestAsync(req, "accept"));
            Assert.Empty(host.OwnedWindows.OfType<FloatingWord>());
            d.OnClosed();
            Assert.Equal(ActResult.Done, await d.AnswerRequestAsync(req, "accept"));
            var word = Assert.Single(host.OwnedWindows.OfType<FloatingWord>());
            Assert.Equal(Loc.Get("friends_add_accepted"), word.GetLogicalDescendants().OfType<TextBlock>().Single().Text);
        }
        finally { foreach (var w in host.OwnedWindows.ToArray()) w.Close(); host.Close(); }
    });
    /// <summary>WPF FriendsDrawer.cs:799-818 + BuildRequestMenu (.Honesty.cs:80): an incoming request's
    /// "more" pill and right-click both carry Block and Report (one line per reason); a report goes to
    /// the wire and its words land on the request row.</summary>
    [Fact]
    public Task ARequestsMoreButtonOffersBlockAndReportByReason() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        d.Render();
        var more = Tagged<Button>(d, "friends-request-more")!;
        Assert.Equal(Loc.Get("friends_action_more"), ToolTip.GetTip(more));
        Assert.Equal(IconKind.MoreHorizontal, more.GetLogicalDescendants().OfType<IconGlyph>().Single().Kind);
        var host = new Window { Width = 600, Height = 500, Content = d };
        try
        {
            host.Show();
            more = Tagged<Button>(d, "friends-request-more")!;   // shown: the row may have been redrawn
            more.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(more.ContextMenu!.IsOpen);   // the click opens the request menu, not just the right-click
            Assert.Equal("friends-request-menu:u_req", more.ContextMenu.Tag);
            more.ContextMenu.Close();
        }
        finally { host.Content = null; host.Close(); }
        var menu = Tagged<Border>(d, "friends-request-in:u_req")!.ContextMenu!;
        var items = menu.ItemsSource!.Cast<MenuItem>().ToList();
        Assert.Equal(new[] { "friends-menu-item:block", "friends-menu-item:report" }, items.Select(i => (string)i.Tag!));
        var reasons = items[1].ItemsSource!.Cast<MenuItem>().ToList();
        Assert.Equal(ReportReason.All.Select(r => "friends-menu-item:report_" + r), reasons.Select(i => (string)i.Tag!));
        reasons[0].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => Tagged<TextBlock>(d, "friends-result") != null);
        Assert.Contains("report", wire.Ops);
        Assert.Contains("u_req", Assert.Single(wire.Reports));
        Assert.DoesNotContain("in:u_req", wire.Reports[0]);
        Assert.Equal(Loc.Get("friends_report_done"), Tagged<TextBlock>(d, "friends-result")!.Text);
    });

    /// <summary>WPF FriendsSfx on the block paths (.Honesty.cs:35/121, .Blocked.cs:143/240-247): a refused
    /// block is denied, a block sinks, an unblock clicks then unlocks.</summary>
    [Fact]
    public Task BlockAndUnblockPlayWpfsCues() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var tags = new List<string>();
        var saved = new CueState();
        saved.Quiet();
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            CoreModArt.AudioOverridePathProvider = _ => typeof(FriendsDrawerTests).Assembly.Location;
            FriendsLanding.SfxGate = new();
            FriendsLanding.SfxNow = SfxStep;   // past every gap: each cue is judged on its own
            CoreSettings.Current.MasterVolume = 50;
            wire.ActSucceeds = false;
            Assert.NotEqual(ActResult.Done, await d.BlockRequesterAsync(svc.Snapshot.Incoming[0]));
            wire.ActSucceeds = true;
            Assert.Equal(ActResult.Done, await d.BlockRequesterAsync(svc.Snapshot.Incoming[0]));
            Assert.Equal(ActResult.Done, await d.ConfirmAsync("u_on", "Mia", "block"));
            Assert.Equal(ActResult.Done, await d.UnblockAsync(new BlockedFriend("u_bad", "Bob")));
            Assert.Equal(new[] { "friends-denied", "friends-sink", "friends-sink", "friends-click", "friends-accepted" }, tags);
        }
        finally
        {
            saved.Restore();
        }
    });

    private sealed class CueState
    {
        private readonly Action<string, float, string, Action<TimeSpan>?, Action?>? _audio = CoreAudio.PlayOneShotProvider;
        private readonly Func<bool>? _suppressed = CoreAudio.IsOutputSuppressedProvider;
        private readonly Func<string, string?>? _art = CoreModArt.AudioOverridePathProvider;
        private readonly FriendsSfxGate _gate = FriendsLanding.SfxGate;
        private readonly Func<DateTime> _now = FriendsLanding.SfxNow;
        private readonly int _volume = CoreSettings.Current.MasterVolume;
        private readonly DateTime _clock = _sfxClock;
        private static readonly System.Reflection.FieldInfo HeldBy = typeof(ConditioningControlPanel.Services.EmiDesk.EmiLineEngine).GetField("_heldBy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        private static readonly System.Reflection.FieldInfo HoldUntil = typeof(ConditioningControlPanel.Services.EmiDesk.EmiLineEngine).GetField("_holdUntilUtc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        private static ConditioningControlPanel.Services.EmiDesk.EmiLineEngine Engine => ConditioningControlPanel.Services.EmiDesk.EmiLineEngine.Instance;
        private readonly object? _heldBy = HeldBy.GetValue(Engine);
        private readonly object? _holdUntil = HoldUntil.GetValue(Engine);
        private readonly ConditioningControlPanel.Services.MandatoryVideoScheduler? _video = CoreEngine.Video;
        /// <summary>FriendsLanding.Sfx is silent under an EMI hold or panic silence, a playing video or a
        /// suppressed output (WPF FriendsSfx.cs:47-51). A test earlier in the shard that failed before its
        /// finally can leave any of them behind (an open Intake page holds "intakeRunning"); a cue test
        /// judges only its own cues, so it starts quiet and Restore puts the leftovers back.</summary>
        public void Quiet()
        {
            HeldBy.SetValue(Engine, null);
            HoldUntil.SetValue(Engine, DateTime.MinValue);
            CoreEngine.Video = null;
            CoreAudio.IsOutputSuppressedProvider = () => false;
        }
        public void AssertRestored()
        {
            Assert.Equal(_heldBy, HeldBy.GetValue(Engine));
            Assert.Equal(_holdUntil, HoldUntil.GetValue(Engine));
            Assert.Same(_video, CoreEngine.Video);
            Assert.Same(_audio, CoreAudio.PlayOneShotProvider);
            Assert.Same(_suppressed, CoreAudio.IsOutputSuppressedProvider);
            Assert.Same(_art, CoreModArt.AudioOverridePathProvider);
            Assert.Same(_gate, FriendsLanding.SfxGate);
            Assert.Same(_now, FriendsLanding.SfxNow);
            Assert.Equal(_volume, CoreSettings.Current.MasterVolume);
            Assert.Equal(_clock, _sfxClock);
        }
        public void Restore()
        {
            HeldBy.SetValue(Engine, _heldBy);
            HoldUntil.SetValue(Engine, _holdUntil);
            CoreEngine.Video = _video;
            CoreAudio.PlayOneShotProvider = _audio;
            CoreAudio.IsOutputSuppressedProvider = _suppressed;
            CoreModArt.AudioOverridePathProvider = _art;
            FriendsLanding.SfxGate = _gate;
            FriendsLanding.SfxNow = _now;
            CoreSettings.Current.MasterVolume = _volume;
            _sfxClock = _clock;
        }
    }

    [Fact]
    public Task CueTestsRestoreStateWithoutAnAssemblyHookBetweenThem() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var original = new CueState();
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => true;
            var expected = new CueState();
            await ReportAndPickerCuesReflectTheWireAndMute();
            expected.AssertRestored();
            await BlockAndUnblockPlayWpfsCues();
            expected.AssertRestored();
            await AnsweringARequestPlaysWpfsCues();
            expected.AssertRestored();
        }
        finally { original.Restore(); }
    });

    /// <summary>WPF AskConfirm (FriendsDrawer.Blocked.cs:75) and ToggleBlocked (:185): opening the Remove
    /// question and the foot's Blocked button each click (restack b38, from #3666).</summary>
    [Fact]
    public Task AskingToRemoveAndTogglingBlockedClick() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var saved = new CueState();
        saved.Quiet();
        var tags = new List<string>();
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            CoreModArt.AudioOverridePathProvider = _ => typeof(FriendsDrawerTests).Assembly.Location;
            FriendsLanding.SfxGate = new();
            FriendsLanding.SfxNow = SfxStep;
            CoreSettings.Current.MasterVolume = 50;
            await d.RunMenuAsync(svc.Snapshot.Friends.First(f => f.Id == "u_on"), "remove");
            Assert.Equal("u_on", d.OpenId);
            d.ToggleBlocked();
            Assert.Equal(new[] { "friends-click", "friends-click" }, tags);
        }
        finally { saved.Restore(); }
    });

    [Fact]
    public Task SquelchAndUnsquelchPlayOnlyTheWireResultCue() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var saved = new CueState();
        saved.Quiet();
        var tags = new List<string>();
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            CoreModArt.AudioOverridePathProvider = _ => typeof(FriendsDrawerTests).Assembly.Location;
            FriendsLanding.SfxGate = new();
            FriendsLanding.SfxNow = SfxStep;
            CoreSettings.Current.MasterVolume = 50;
            var friend = svc.Snapshot.Friends.First(f => f.Id == "u_on");
            await d.RunMenuAsync(friend, "squelch");
            await d.RunMenuAsync(friend, "unsquelch");
            wire.ActSucceeds = false;
            await d.RunMenuAsync(friend, "squelch");
            Assert.Equal(new[] { "friends-click", "friends-click", "friends-denied" }, tags);
        }
        finally { saved.Restore(); }
    });

    /// <summary>Each cue test uses a fresh gate and steps past every gap. CueState restores both
    /// the gate and the clock so no test changes the next one's timing.</summary>
    private static DateTime _sfxClock = DateTime.UtcNow;
    private static DateTime SfxStep() => _sfxClock = (_sfxClock > DateTime.UtcNow ? _sfxClock : DateTime.UtcNow).AddSeconds(2);

    /// <summary>WPF AnswerRequestAsync (FriendsDrawer.Honesty.cs:52-74): every answer clicks; a refused one
    /// is denied, an accept unlocks, a decline or withdraw sinks.</summary>
    [Fact]
    public Task AnsweringARequestPlaysWpfsCues() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var r = svc.Snapshot.Incoming[0];
        var tags = new List<string>();
        var saved = new CueState();
        saved.Quiet();
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            CoreModArt.AudioOverridePathProvider = _ => typeof(FriendsDrawerTests).Assembly.Location;
            FriendsLanding.SfxGate = new();
            FriendsLanding.SfxNow = SfxStep;
            CoreSettings.Current.MasterVolume = 50;
            wire.ActSucceeds = false;
            Assert.NotEqual(ActResult.Done, await d.AnswerRequestAsync(r, "accept"));
            wire.ActSucceeds = true;
            Assert.Equal(ActResult.Done, await d.AnswerRequestAsync(r, "accept"));
            Assert.Equal(ActResult.Done, await d.AnswerRequestAsync(r, "decline"));
            Assert.Equal(new[] { "friends-click", "friends-denied", "friends-click", "friends-accepted", "friends-click", "friends-sink" }, tags);
        }
        finally
        {
            saved.Restore();
        }
    });

    /// <summary>WPF BackRoomHostService.cs:385/765 and EnhancementPlayerWindow.xaml.cs:113-114: the Breakout
    /// window and the Deeper player are the friends activity while they are on screen, and leave it on close.</summary>
    [Fact]
    public Task TheBreakoutAndDeeperWindowsArePresenceWhileOpen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (svc, _) = Service();
        var oldHead = FriendsHead.Service;
        global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow? game = null;
        global::ConditioningControlPanel.Avalonia.Views.Deeper.EnhancementPlayerWindow? player = null;
        try
        {
            FriendsHead.Service = svc;
            game = new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow();
            game.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(PresenceActivity.Breakout, svc.Activity);
            player = new global::ConditioningControlPanel.Avalonia.Views.Deeper.EnhancementPlayerWindow(null, null);
            player.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(PresenceActivity.Deeper, svc.Activity);
            game.Close();
            Assert.Equal(PresenceActivity.Deeper, svc.Activity);
            player.Close();
            Assert.Equal(PresenceActivity.Panel, svc.Activity);
        }
        finally
        {
            try { game?.Close(); player?.Close(); }
            finally { FriendsHead.Service = oldHead; }
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task SessionPresenceRestoresTheUnderlyingActivity() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (svc, _) = Service();
        var oldHead = FriendsHead.Service;
        var oldSessions = global::ConditioningControlPanel.Avalonia.App.Sessions;
        var runner = new ConditioningControlPanel.Services.SessionRunner(new ConditioningControlPanel.Services.SessionLogService());
        global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? shell = null;
        try
        {
            FriendsHead.Service = svc;
            global::ConditioningControlPanel.Avalonia.App.Sessions = runner;
            svc.EnterActivity(PresenceActivity.Deeper);
            FriendsHead.WatchSessionStop(runner); // Startup subscription is pinned separately by FriendsPresenceWiringTests.
            shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            shell.StartSession(new Session { Id = "presence-test", Name = "Presence", DurationMinutes = 5 });
            Assert.Equal(PresenceActivity.Session, svc.Activity);
            runner.Stop();
            Assert.Equal(PresenceActivity.Deeper, svc.Activity);
        }
        finally
        {
            try { runner.Stop(); shell?.Close(); }
            finally
            {
                FriendsHead.Service = oldHead;
                global::ConditioningControlPanel.Avalonia.App.Sessions = oldSessions;
            }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReportAndPickerCuesReflectTheWireAndMute() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var tags = new List<string>();
        var saved = new CueState();
        saved.Quiet();
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            CoreModArt.AudioOverridePathProvider = _ => typeof(FriendsDrawerTests).Assembly.Location;
            FriendsLanding.SfxGate = new();
            FriendsLanding.SfxNow = SfxStep;
            CoreSettings.Current.MasterVolume = 50;
            wire.ActSucceeds = false;
            Assert.NotEqual(ActResult.Done, await d.ReportAsync("in:u_req", "u_req", ReportReason.All[0]));
            Assert.Equal(Loc.Get(FriendsDrawerRules.ActResultKey(ActResult.TryLater)), Tagged<TextBlock>(d, "friends-result")?.Text);
            Assert.Equal(new[] { "friends-click", "friends-denied" }, tags);
            tags.Clear();
            d.Toggle("u_on");
            Tagged<Button>(d, "friends-action:poke")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { "friends-click" }, tags);
            tags.Clear();
            await d.PokeAsync("u_on", PokeSet.Shipped[0]);
            Assert.Equal(new[] { "friends-sent" }, tags);
            tags.Clear();
            CoreAudio.IsOutputSuppressedProvider = () => true;
            await d.PokeAsync("u_on", PokeSet.Shipped[0]);
            Assert.Empty(tags);
        }
        finally { saved.Restore(); }
    });

    private sealed class PresenceRelay : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(r.RequestUri!.AbsolutePath.EndsWith("/start") ? "{\"code\":\"ABC123\"}" : "{}") });
    }

    [Fact]
    public Task RemotePresenceStartsAfterPairingAndEndsWithTheSession() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var oldHead = FriendsHead.Service;
        var oldRelay = global::ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView.Relay;
        using var relay = new ConditioningControlPanel.Services.RemoteRelay(() => "token", () => "test", "test",
            (_, _) => null, _ => { }, new PresenceRelay()) { AutoPoll = false };
        global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? shell = null;
        try
        {
            FriendsHead.Service = svc;
            svc.EnterActivity(PresenceActivity.Deeper);
            global::ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView.Relay = new Lazy<ConditioningControlPanel.Services.RemoteRelay>(() => relay);
            shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            var tab = new global::ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView();
            await tab.StartAsync(shell, "light");
            Assert.Equal(PresenceActivity.Remote, svc.Activity);
            shell.OnRemoteSessionEnded();
            Assert.Equal(PresenceActivity.Deeper, svc.Activity);
        }
        finally
        {
            try { await relay.StopAsync(); shell?.Close(); }
            finally
            {
                FriendsHead.Service = oldHead;
                global::ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView.Relay = oldRelay;
            }
        }
    });

    [Fact]
    public Task RequestReportUsesTheAccountIdAndWordsTheRequestRow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        d.Render();
        var menu = Tagged<Button>(d, "friends-request-more")!.ContextMenu!;
        Assert.Equal(global::Avalonia.Controls.PlacementMode.Bottom, menu.Placement);
        var items = menu.ItemsSource!.Cast<MenuItem>().ToList();
        Assert.Equal(new[] { "friends-menu-item:block", "friends-menu-item:report" }, items.Select(i => (string)i.Tag!));
        var reasons = items[1].ItemsSource!.Cast<MenuItem>().ToList();
        Assert.Equal(ReportReason.All.Select(r => "friends-menu-item:report_" + r), reasons.Select(i => (string)i.Tag!));
        reasons[0].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => Tagged<TextBlock>(d, "friends-result") != null);
        Assert.Contains("u_req", Assert.Single(wire.Reports));
        Assert.DoesNotContain("in:u_req", wire.Reports[0]);
        Assert.Equal(Loc.Get("friends_report_done"), Tagged<TextBlock>(d, "friends-result")!.Text);
    });

}
