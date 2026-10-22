using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Serilog;
using RowItem = ConditioningControlPanel.Services.Startup.InboxItem;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// PORTED from ConditioningControlPanel/Services/Friends/FriendsLanding.cs: what a delivery does when it lands.
/// Core's <see cref="FriendsLandingRouter"/> decides (hold, Inbox, knock); this sink knocks an invite with a
/// <see cref="KnockCard"/>, puts pokes, watches and friend requests up as corner <see cref="FriendNotices"/>, files
/// Inbox rows, and throws the words a delivery or a send leaves through <see cref="FloatingWord"/> (poke received with
/// Emi's line, the sent beat, Say/Tell from outside the drawer). Nothing here plays or joins by itself. A light timer
/// re-attaches to <see cref="FriendsHead.Service"/> and releases the held queue (WPF :28-82).
/// ponytail: the watch actions (Deeper player, flavour apply, HT) are not on this head, so a watch notice carries no
/// Watch / Try it button (a press that opens nothing); its body still opens the drawer and it waits in the Inbox.
/// </summary>
internal static class FriendsLanding
{
    private static DispatcherTimer? _timer;
    private static FriendsLandingRouter? _router;
    private static readonly Sink TheSink = new();

    /// <summary>The world the router reads; tests replace it (WPF ReadWorld :86).</summary>
    internal static Func<LandingWorld> World { get; set; } = ReadWorld;

    /// <summary>Where a card lands: the panel, else the launcher (WPF Anchor :158); tests replace it.</summary>
    internal static Func<Window?> Anchor { get; set; } = DefaultAnchor;

    /// <summary>WPF ActiveGameWindow: the active window when it is a game host's; tests replace it.</summary>
    internal static Func<Window?> ActiveGameWindow { get; set; } = () => GameWindow.ActiveOpen();

    /// <summary>WPF EmiSays: Emi's desk line when she is out; tests replace it.</summary>
    internal static Action<string, string> EmiSays { get; set; } = DeskSays;

    /// <summary>WPF OpenDrawer: the panel up and the friends drawer open on a friend; tests replace it.</summary>
    internal static Action<string?> OpenDrawer { get; set; } = DefaultOpenDrawer;

    /// <summary>The friends service the landing follows; tests replace it.</summary>
    internal static Func<IFriendsService?> Service { get; set; } = () => FriendsHead.Service;

    internal static FriendsLandingRouter? Router => _router;

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Normal, (_, _) => Tick());
        Tick();
    }

    /// <summary>Exit path (WPF Stop :38): no landing window may outlive the app.</summary>
    public static void Stop()
    {
        try { _timer?.Stop(); } catch { /* shutting down */ }
        _timer = null;
        _router?.Dispose();
        _router = null;
        try { KnockCard.CloseAll(); } catch { /* shutting down */ }
        try { FriendNotices.CloseAll(); } catch { /* shutting down */ }
    }

    /// <summary>One timer tick (WPF Tick :48): follow the current service, release the held queue, and
    /// fold any card still up when a hold (lockdown, Strict Lock, a session) starts.</summary>
    internal static void Tick()
    {
        try
        {
            var current = Service();
            if (!ReferenceEquals(_router?.Service, current))
            {
                _router?.Dispose();
                // The server's clock: an invite's at / expires_at are server times.
                _router = current == null ? null : new FriendsLandingRouter(current, () => World(), () => KnockCard.Clock(), TheSink);
            }
            _router?.Release();
            // A notice already up when a lockdown or a session starts goes on the next tick (WPF :66).
            if (FriendNotices.AnyUp && World().Holding) FriendNotices.CloseAll(fold: true);
            if (KnockCard.AnyUp && World().Holding) KnockCard.FoldAll();
        }
        catch (Exception ex) { Log.Debug("[Friends] landing tick: {E}", ex.Message); }
    }

    private static LandingWorld ReadWorld()
    {
        var session = CoreSession.IsSessionRunning;
        bool lockdown = false, program = false;
        try { lockdown = LockdownService.Current?.IsActive == true; } catch { /* not built yet */ }
        try { program = session && App.Programs?.ActiveEnrollment != null; } catch { /* not built yet */ }
        var mw = MainShellWindow.Current;
        var lw = LauncherWindow.Instance;
        return new LandingWorld(
            Lockdown: lockdown,
            StrictLock: session && CoreSettings.Current.StrictLockEnabled,
            ProgramSession: program,
            PanelVisible: mw is { IsVisible: true } && mw.WindowState != WindowState.Minimized,
            LauncherVisible: lw is { IsVisible: true } && lw.WindowState != WindowState.Minimized,
            GameHostActive: GameWindow.ActiveOpen() != null,
            SessionRunning: session);
    }

    private static Window? DefaultAnchor()
    {
        var mw = MainShellWindow.Current;
        if (mw is { IsVisible: true } && mw.WindowState != WindowState.Minimized) return mw;
        var lw = LauncherWindow.Instance;
        return lw is { IsVisible: true } && lw.WindowState != WindowState.Minimized ? lw : null;
    }

    /// <summary>WPF FriendsLanding.Tell/Say: a short word where the player is looking (the game, the panel,
    /// the launcher). <paramref name="fallback"/> is the caller's own window when none of those is up.</summary>
    internal static void Tell(string text, bool good, Window? fallback = null)
    {
        var anchor = ActiveGameWindow() ?? Anchor() ?? fallback;
        if (anchor != null) FloatingWord.Throw(anchor, text, pink: !good, small: true);
    }

    private static void DeskSays(string line, string face)
    {
        try
        {
            var desk = EmiDeskService.Instance;
            if (desk.IsOut && desk.Window != null) desk.Window.Say(line, face);
        }
        catch (Exception ex) { Log.Debug("[Friends] emi say: {E}", ex.Message); }
    }

    // ---------------------------------------------------------------- words (WPF :169-201)

    private static string Str(string key, string english)
    {
        try { var v = Loc.Get(key); return string.IsNullOrWhiteSpace(v) || v == key ? english : v; }
        catch { return english; }
    }

    private static string DestinationName(string? dest) => dest switch
    {
        InviteDestination.Goon => Str("friends_land_dest_goon", "the Goon Game"),
        InviteDestination.Ramp => Str("friends_land_dest_ramp", "a Ramp link"),
        InviteDestination.Chess => Str("friends_land_dest_chess", "a game of chess"),
        _ => Str("friends_land_dest_backroom", "the Back Room"),
    };

    internal static string KnockLine(InboxItem item)
    {
        if (item.Kind == SendKind.Invite)
            return string.Format(Str("friends_land_invite_line", "invites you to {0}"), DestinationName(item.Destination));
        return item.Watch?.Kind == WatchKind.Flavour ? Str("friends_land_flavour_line", "sends you a flavour") : Str("friends_land_watch_line", "sends you a watch");
    }

    private static void ShowCard(Window anchor, InboxItem item) =>
        // No Join where this head has no host: a Join that opens nothing would tell the sender "joined".
        KnockCard.Show(anchor, item, KnockLine(item), CanJoin(item) ? Str("friends_land_join", "Join") : null,
            Str("friends_land_not_now", "Not now"), Str("friends_land_answer_later", "Answer later"),
            OnKnockDone, shown: i => FriendsSeen.Shared.Seen(Service(), i.Id));

    private sealed class Sink : ILandingSink
    {
        /// <summary>WPF Sink.Poke :212: the corner notice with Poke back (over the game when one has the screen);
        /// not over a game, Emi says it and the word floats over the panel, pink for a tease. Bell off: the Inbox row.</summary>
        public void Poke(InboxItem item, bool inGame)
        {
            if (!NoticesOn) { Inbox(item); return; }
            var word = Str("friends_poke_" + item.PokeId, LandingRules.PokeFallback(item.PokeId));
            var pink = LandingRules.PokeIsPink(item.PokeId);
            Sfx("bubbles/Pop3.mp3", inGame ? 0.10f : 0.16f, "friends-poke-in", incoming: true);   // WPF FriendsSfx.PokeIn
            var game = inGame ? ActiveGameWindow() : null;
            FriendNotices.Show(game ?? Anchor(), Notice(NoticeKind.Poke, item.FromId, item.FromName, item, item.At), new NoticeLook
            {
                Line = Str("friends_notice_poked", "poked you:"),
                Word = word,
                Pink = pink,
                ActionLabel = Str("friends_notice_poke_back", "Poke back"),
                Act = p => { if (p is InboxItem i) PokeBack(i); },
                Open = p => OpenDrawer((p as InboxItem)?.FromId),
                // Ran out unseen (or pushed off the stack): it waits in the Inbox instead of vanishing.
                Missed = p => { if (p is InboxItem i) Inbox(i); },
            });
            FriendsSeen.Shared.Seen(Service(), item.Id);
            if (game != null) return;
            EmiSays(string.Format(Str("friends_land_emi_poke", "{0} says {1}"), item.FromName, word), LandingRules.PokeFace(item.PokeId));
            if (Anchor() is { } anchor) FloatingWord.Throw(anchor, word, pink);
        }

        public void Knock(InboxItem item, bool inGame)
        {
            var anchor = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();   // WPF :262
            // The bell off, or nothing of ours on screen: the Inbox row and nothing else (WPF :263-264).
            if (anchor == null || !NoticesOn) { Inbox(item); return; }
            Sfx("chaos/dling.mp3", 0.18f, "friends-knock", incoming: true);   // WPF FriendsSfx.Knock
            if (!inGame)   // WPF :266-270
                EmiSays(item.Kind == SendKind.Invite ? Str("friends_land_emi_knock", "someone wants you") : Str("friends_land_emi_present", "a present"),
                    item.Kind == SendKind.Invite ? "o_o" : "^_~");
            // An invite knocks with a card that counts down to its own expiry (WPF :251-258).
            if (item.Kind == SendKind.Invite) { ShowCard(anchor, item); return; }
            // A watch knocks with a corner notice (WPF :261); no Watch / Try it here (class note).
            FriendNotices.Show(anchor, Notice(NoticeKind.Watch, item.FromId, item.FromName, item, item.At), new NoticeLook
            {
                Line = KnockLine(item),
                Open = p => OpenDrawer((p as InboxItem)?.FromId),
                // Still answerable after the toast goes: it waits in the Inbox, where it reopens as its notice.
                Left = p => { if (p is InboxItem i && !i.IsExpired(KnockCard.Clock())) Inbox(i); },
            });
            FriendsSeen.Shared.Seen(Service(), item.Id);
        }

        public void Inbox(InboxItem item)
        {
            var title = item.Kind == SendKind.Poke
                ? string.Format(Str("friends_land_inbox_poke", "{0}: {1}"), item.FromName,
                    Str("friends_poke_" + item.PokeId, LandingRules.PokeFallback(item.PokeId)))
                : item.FromName;
            StartupLadder.Inbox.File(new RowItem
            {
                Key = "friends:" + item.Id,
                Title = title,
                Summary = item.Kind == SendKind.Poke ? "" : KnockLine(item),
                Glyph = item.Kind == SendKind.Poke ? "✨" : item.Kind == SendKind.Invite ? "🚪" : "🎁",
                Open = () => Reopen(item),
            });
        }

        /// <summary>WPF Sink.SentBeat :366: "knock sent" / "watch sent" / "sent", small and mint.</summary>
        public void SentBeat(SendKind kind, Friend to)
        {
            var anchor = ActiveGameWindow() ?? Anchor();
            if (anchor == null) return;
            var (key, english) = LandingRules.SentWord(kind);
            FloatingWord.Throw(anchor, Str(key, english), pink: false, small: true);
        }

        /// <summary>WPF Sink.RequestRow :296: every request files an Inbox row that opens the drawer.</summary>
        public void RequestRow(FriendRequest request) => StartupLadder.Inbox.File(new RowItem
        {
            Key = RequestKey(request.Id),
            Title = request.Name,
            Summary = Str("friends_land_request_line", "wants to be friends"),
            Glyph = "💌",
            Open = () => OpenDrawer(null),
        });

        /// <summary>WPF Sink.RequestAnnounce :308: the request's cue and a notice with Accept.</summary>
        public void RequestAnnounce(FriendRequest request, bool inGame)
        {
            if (!NoticesOn) return;
            var owner = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
            if (owner == null) return;
            RequestSfx();
            FriendNotices.Show(owner, Notice(NoticeKind.Request, request.Id, request.Name, request, request.At), new NoticeLook
            {
                Line = Str("friends_land_request_line", "wants to be friends"),
                ActionLabel = Str("friends_notice_accept", "Accept"),
                Act = p => { if (p is FriendRequest r) Accept(r); },
                Open = _ => OpenDrawer(null),
            });
            FriendsSeen.Shared.RequestSeen(Service(), request);
        }

        public void RequestCue()
        {
            if (NoticesOn) RequestSfx();
        }

        public void RequestGone(string requestId) => StartupLadder.Inbox.Remove(RequestKey(requestId));

        /// <summary>WPF Sink.RequestsWaiting :333: one Inbox row for the requests already waiting, and one notice
        /// when <paramref name="announce"/> (by name for one, "N friend requests waiting" for more).</summary>
        public void RequestsWaiting(IReadOnlyList<FriendRequest> waiting, bool announce, bool inGame)
        {
            if (waiting.Count == 0) { StartupLadder.Inbox.Remove(WaitingKey); return; }
            var newest = waiting[0];
            foreach (var r in waiting) if (r.At > newest.At) newest = r;
            var title = waiting.Count == 1 ? newest.Name : string.Format(Str("friends_land_waiting_many", "{0} friend requests waiting"), waiting.Count);
            var summary = waiting.Count == 1 ? Str("friends_land_request_line", "wants to be friends") : "";
            StartupLadder.Inbox.File(new RowItem { Key = WaitingKey, Title = title, Summary = summary, Glyph = "💌", Open = () => OpenDrawer(null) });
            if (!announce || !NoticesOn) return;
            var owner = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
            if (owner == null) return;
            RequestSfx();
            FriendNotices.Show(owner, Notice(NoticeKind.Request, WaitingKey, waiting.Count == 1 ? newest.Name : "", newest, newest.At), new NoticeLook
            {
                Line = waiting.Count == 1 ? summary : title,
                ActionLabel = Str("friends_land_waiting_open", "Open"),
                Act = _ => OpenDrawer(null),
                Open = _ => OpenDrawer(null),
            });
            // One request by name on screen is seen; "3 friend requests waiting" names nobody.
            if (waiting.Count == 1) FriendsSeen.Shared.RequestSeen(Service(), newest);
        }
    }

    private static string RequestKey(string id) => "friends-request:" + id;

    private const string WaitingKey = "friends-requests-waiting";

    private static void RequestSfx() => Sfx("chaos/dling.mp3", 0.16f, "friends-request", incoming: true);   // WPF FriendsSfx.Request

    /// <summary>A notice dated by when it was SENT (the server's <c>at</c>), not when it landed here (WPF :404).</summary>
    private static FriendNotice Notice(NoticeKind kind, string friendId, string name, object payload, DateTimeOffset sent)
        => new(kind, friendId, name, LandingRules.NoticeAt(sent, DateTimeOffset.UtcNow), FriendNoticeRules.LifetimeMs(kind), payload);

    /// <summary>WPF PokeBack :418: the same preset straight back; a good send has the router's sent beat.</summary>
    private static async void PokeBack(InboxItem item)
    {
        try
        {
            var svc = Service();
            if (svc == null || string.IsNullOrEmpty(item.FromId)) return;
            Sfx("chaos/ui_click.mp3", 0.12f, "friends-click");
            var r = await svc.PokeAsync(item.FromId, string.IsNullOrEmpty(item.PokeId) ? "hi" : item.PokeId);
            if (ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.IsGood(r)) return;
            Sfx("chaos/ui_denied.mp3", 0.14f, "friends-denied");
            Tell(Loc.Get(ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.SendResultKey(r)), good: false);
        }
        catch (Exception ex) { Log.Debug("[Friends] poke back: {E}", ex.Message); }
    }

    /// <summary>WPF Accept :437.</summary>
    private static async void Accept(FriendRequest request)
    {
        try
        {
            var svc = Service();
            if (svc == null) return;
            Sfx("chaos/ui_click.mp3", 0.12f, "friends-click");
            var r = await svc.AcceptAsync(request.Id);
            if (r == ActResult.Done)
            {
                Sfx("chaos/ui_unlock.mp3", 0.18f, "friends-accepted");
                Tell(Loc.Get("friends_add_accepted"), good: true);
                return;
            }
            Sfx("chaos/ui_denied.mp3", 0.14f, "friends-denied");
            Tell(Loc.Get(ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.ActResultKey(r)), good: false);
        }
        catch (Exception ex) { Log.Debug("[Friends] accept: {E}", ex.Message); }
    }

    /// <summary>WPF OpenDrawer :457: the panel up, then the rail chip's drawer after a layout pass.</summary>
    private static void DefaultOpenDrawer(string? friendId)
    {
        try
        {
            if (MainShellWindow.Current is not { } mw) return;
            if (!mw.IsVisible || mw.WindowState == WindowState.Minimized) LauncherWindow.OpenPanel(mw);
            // After a layout pass: a popup placed on a hidden chip lands at 0,0.
            Dispatcher.UIThread.Post(() =>
            {
                try { mw.Named<FriendsRailChip>("FriendsChip")?.OpenDrawer(friendId); }
                catch (Exception ex) { Log.Debug("[Friends] open drawer: {E}", ex.Message); }
            }, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex) { Log.Debug("[Friends] open drawer: {E}", ex.Message); }
    }

    /// <summary>The drawer's bell (WPF NoticesOn :348). Off: Inbox rows only.</summary>
    private static bool NoticesOn => CoreSettings.Current.FriendNotificationsEnabled;

    /// <summary>An Inbox row opened later: the card again while it is still answerable (WPF Reopen :465).
    /// Opened by hand, so it comes up whatever the bell says.</summary>
    private static void Reopen(InboxItem item)
    {
        if (item.IsExpired(KnockCard.Clock())) { EmiSays(Str("friends_land_too_late", "that one already left"), ";_;"); return; }
        if (item.Kind == SendKind.Poke) TheSink.Poke(item, inGame: false);
        else if (item.Kind == SendKind.Invite && Anchor() is { } anchor) ShowCard(anchor, item);
        else TheSink.Knock(item, inGame: false);   // WPF :478: a watch's notice, or no anchor -> the row again
    }

    /// <summary>How a card ended (WPF OnKnockDone :487).</summary>
    internal static void OnKnockDone(InboxItem item, KnockOutcome outcome)
    {
        var now = KnockCard.Clock();
        switch (outcome)
        {
            case KnockOutcome.Later:
                if (!item.IsExpired(now)) TheSink.Inbox(item);
                return;
            case KnockOutcome.RanOut:
                // An invite that ran out says nothing here; the server tells the sender "no answer".
                if (item.Kind == SendKind.Watch && !item.IsExpired(now)) TheSink.Inbox(item);
                return;
            case KnockOutcome.NotNow:
                FriendsSeen.Shared.Report(Service(), ReceiptReport.Item(item.Id, ReceiptState.Declined));
                StartupLadder.Inbox.Remove("friends:" + item.Id);
                Sfx("chaos/sink.mp3", 0.10f, "friends-sink");   // WPF FriendsSfx.Dismiss
                return;
        }
        if (item.Kind != SendKind.Invite) { StartupLadder.Inbox.Remove("friends:" + item.Id); return; }
        if (!CanJoin(item)) { OnKnockDone(item, KnockOutcome.Later); return; }   // never a Joined receipt for nothing
        StartupLadder.Inbox.Remove("friends:" + item.Id);
        FriendsSeen.Shared.Report(Service(), ReceiptReport.Item(item.Id, ReceiptState.Joined));
        Sfx("chaos/reveal_chime.mp3", 0.20f, "friends-join");   // WPF FriendsSfx.Join
        Join(item);
    }

    /// <summary>Destinations this head can open. ponytail: Back Room / Goon / chess hosts are not on this head
    /// (GameWindow has the Breakout demo only), so their cards carry Not now and the x only.</summary>
    internal static bool CanJoin(InboxItem item) => item.Destination == InviteDestination.Ramp;

    /// <summary>WPF LauncherHost.OpenPanelTab: the panel up (it may be hidden behind the launcher), then the tab.
    /// Tests replace it.</summary>
    internal static Action<string> OpenPanelTab { get; set; } = tab =>
    {
        if (MainShellWindow.Current is not { } mw) return;
        LauncherWindow.OpenPanel(mw);
        mw.ShowTab(tab);
    };

    /// <summary>WPF Join :522. Ramp: link to Ramp lives on the Ramp card in the Studio tab.</summary>
    private static void Join(InboxItem item)
    {
        Log.Information("[Friends] joining {Dest} from a knock", item.Destination);
        if (item.Destination == InviteDestination.Ramp) OpenPanelTab("studio");
    }

    private static readonly FriendsSfxGate SfxGate = new();

    /// <summary>WPF FriendsSfx.Play: master volume, the 1.5 s incoming gap, half level in a session.</summary>
    private static void Sfx(string rel, float scale, string tag, bool incoming = false)
    {
        try
        {
            var s = CoreSettings.Current;
            if (s.MasterVolume <= 0) return;
            lock (SfxGate) if (!SfxGate.TryPass(DateTime.UtcNow, incoming)) return;
            var path = CoreModArt.AudioOverridePath(rel)
                ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var level = s.MasterVolume / 100f * scale * FriendsSfxGate.Level(incoming, CoreSession.IsSessionRunning);
            CoreAudio.PlayOneShot(path, Math.Clamp(level, 0f, 1f), tag);
        }
        catch (Exception ex) { Log.Debug(ex, "[Friends] sfx {Tag} failed", tag); }
    }
}
