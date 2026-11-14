using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Launcher;
using Serilog;
using RowItem = ConditioningControlPanel.Services.Startup.InboxItem;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// PORTED (invites, pokes and watches) from ConditioningControlPanel/Services/Friends/FriendsLanding.cs: what a delivery
/// does when it lands. Core's <see cref="FriendsLandingRouter"/> decides (hold, Inbox, knock); this sink
/// knocks an invite with a <see cref="KnockCard"/>, files Inbox rows, and throws the words a delivery or a send
/// leaves through <see cref="FloatingWord"/> (poke received with Emi's line, the sent beat, Say/Tell from outside
/// the drawer). A light timer re-attaches to <see cref="FriendsHead.Service"/>
/// and releases the held queue (WPF :28-82).
/// Request, poke and watch corner notices are in FriendsLanding.Notices.cs.
/// </summary>
internal static partial class FriendsLanding
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
    // WPF Services/Friends/FriendsLanding.cs:38-46,66: exit and hold close notices.
    public static void Stop()
    {
        try { _timer?.Stop(); } catch { /* shutting down */ }
        _timer = null;
        _router?.Dispose();
        _router = null;
        try { FriendNotices.CloseAll(); } catch { /* shutting down */ }
        try { KnockCard.CloseAll(); } catch { /* shutting down */ }
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
            if (World().Holding) { KnockCard.FoldAll(); FriendNotices.CloseAll(fold: true); }
            // The server lists blocks now: carry this PC's old list over once (WPF FriendsLanding.cs:71).
            if (current != null) global::ConditioningControlPanel.Controls.Friends.FriendsBlockList.MigrateIfDue(current);
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
        InviteDestination.Race => Str("friends_land_dest_race", "Racing Thoughts"),
        InviteDestination.Breakout => Str("friends_land_dest_breakout", "Breakout"),
        _ => Str("friends_land_dest_backroom", "the Back Room"),
    };

    internal static string KnockLine(InboxItem item)
    {
        if (item.Kind == SendKind.Invite)
            return string.Format(Str("friends_land_invite_line", "invites you to {0}"), LandingRules.WithCourse(DestinationName(item.Destination), item.Track));
        return item.Watch?.Kind == WatchKind.Flavour ? Str("friends_land_flavour_line", "sends you a flavour") : Str("friends_land_watch_line", "sends you a watch");
    }

    private static void ShowCard(Window anchor, InboxItem item) =>
        // No Join where this head has no host: a Join that opens nothing would tell the sender "joined".
        KnockCard.Show(anchor, item, KnockLine(item), CanJoin(item) ? Str("friends_land_join", "Join") : null,
            Str("friends_land_not_now", "Not now"), Str("friends_land_answer_later", "Answer later"),
            OnKnockDone, shown: i => FriendsSeen.Shared.Seen(Service(), i.Id));

    private sealed class Sink : ILandingSink
    {
        /// <summary>WPF Sink.Poke :229-258: the corner notice with Poke back; outside a game Emi says it and the
        /// word floats over the panel, pink for a tease. Bell off: the Inbox row and nothing else, not even the cue.</summary>
        public void Poke(InboxItem item, bool inGame)
        {
            if (!NoticesOn) { Inbox(item); return; }
            Sfx("bubbles/Pop3.mp3", inGame ? 0.10f : 0.16f, "friends-poke-in", incoming: true);   // WPF FriendsSfx.PokeIn
            var game = inGame ? ActiveGameWindow() : null;
            var anchor = Anchor();
            if (!ShowPoke(game ?? anchor, item)) { Inbox(item); return; }
            if (game != null || anchor == null) return;
            var word = Str("friends_poke_" + item.PokeId, LandingRules.PokeFallback(item.PokeId));
            EmiSays(string.Format(Str("friends_land_emi_poke", "{0} says {1}"), item.FromName, word),
                LandingRules.PokeFace(item.PokeId));
            FloatingWord.Throw(anchor, word, LandingRules.PokeIsPink(item.PokeId));
        }

        public void Knock(InboxItem item, bool inGame)
        {
            var anchor = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();   // WPF :262
            // The bell off, or nothing of ours on screen: the Inbox row and nothing else (WPF :263-264).
            if (anchor == null || !NoticesOn) { Inbox(item); return; }
            Sfx("chaos/dling.mp3", 0.18f, "friends-knock", incoming: true);   // WPF FriendsSfx.Knock
            if (!inGame) EmiSays(item.Kind == SendKind.Invite
                ? Str("friends_land_emi_knock", "someone wants you") : Str("friends_land_emi_present", "a present"),
                item.Kind == SendKind.Invite ? "o_o" : "^_~");   // WPF :266-270
            if (item.Kind == SendKind.Invite) ShowCard(anchor, item);
            else ShowWatchNotice(anchor, item);
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
        public void RequestRow(FriendRequest request) => FileRequest(request);
        public void RequestAnnounce(FriendRequest request, bool inGame) => AnnounceRequest(request, inGame);
        public void RequestCue() { if (NoticesOn) Sfx("chaos/dling.mp3", 0.16f, "friends-request", true); }
        public void RequestGone(string requestId) => StartupLadder.Inbox.Remove("friends-request:" + requestId);
        public void RequestsWaiting(IReadOnlyList<FriendRequest> waiting, bool announce, bool inGame) => Waiting(waiting, announce, inGame);
    }

    /// <summary>The drawer's bell (WPF NoticesOn :348). Off: Inbox rows only.</summary>
    private static bool NoticesOn => CoreSettings.Current.FriendNotificationsEnabled;

    /// <summary>An Inbox row opened later: the card again while it is still answerable (WPF Reopen :465).
    /// An invite opened by hand comes up whatever the bell says; pokes and watches follow the bell (WPF :482-490).</summary>
    private static void Reopen(InboxItem item)
    {
        if (item.IsExpired(KnockCard.Clock()))
        {
            EmiSays(Str("friends_land_too_late", "that one already left"), ";_;");
            return;
        }
        // WPF Reopen :487: a poke row goes back through the arrival path, bell included.
        if (item.Kind == SendKind.Poke) { TheSink.Poke(item, inGame: false); return; }
        if (item.Kind == SendKind.Invite)
        {
            if (Anchor() is { } anchor) ShowCard(anchor, item);
            else TheSink.Inbox(item);   // WPF :478 Knock -> no anchor -> the row again
        }
        else if (item.Kind == SendKind.Watch) TheSink.Knock(item, inGame: false);   // WPF Reopen :482-490: its corner notice again
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
        if (item.Kind != SendKind.Invite)
        {
            StartupLadder.Inbox.Remove("friends:" + item.Id);
            if (item.Watch != null) Watch(item.Watch);
            return;
        }
        if (!CanJoin(item)) { OnKnockDone(item, KnockOutcome.Later); return; }   // never a Joined receipt for nothing
        StartupLadder.Inbox.Remove("friends:" + item.Id);
        if (item.Destination == InviteDestination.Race && !OpenRace(item.Room))
        { FriendsSeen.Shared.Report(Service(), ReceiptReport.Item(item.Id, ReceiptState.Declined)); return; }
        FriendsSeen.Shared.Report(Service(), ReceiptReport.Item(item.Id, ReceiptState.Joined));
        Sfx("chaos/reveal_chime.mp3", 0.20f, "friends-join");   // WPF FriendsSfx.Join
        Join(item);
    }

    /// <summary>Destinations this head can open. Back Room and Goon still wait for their hosts; Breakout uses the
    /// demo until a full host exists; Racing Thoughts opens the race window with the friend's room.</summary>
    internal static bool CanJoin(InboxItem item) => item.Destination == InviteDestination.Ramp
        || item.Destination == InviteDestination.Race
        || item.Destination == InviteDestination.Chess && InviteDestination.IsChallengeId(item.Code)
        || item.Destination == InviteDestination.Breakout && (!BreakoutFullAllowed || GameWindow.Games.ContainsKey("breakout"));

    private static bool BreakoutFullAllowed => TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
    internal static Action<string> LaunchGame { get; set; } = id => GameWindow.Launch(id);

    /// <summary>WPF LauncherHost.OpenPanelTab: the panel up (it may be hidden behind the launcher), then the tab.
    /// Tests replace it.</summary>
    internal static Action<string> OpenPanelTab { get; set; } = tab =>
    {
        if (MainShellWindow.Current is not { } mw) return;
        LauncherWindow.OpenPanel(mw);
        mw.ShowTab(tab);
    };

    /// <summary>WPF Services/Friends/FriendsLanding.cs:522-581. Ramp: link to Ramp lives on the Ramp card in the Studio tab;
    /// Chess accepts a validated challenge; Breakout uses the station's full/demo door.</summary>
    internal static void Join(InboxItem item)
    {
        Log.Information("[Friends] joining {Dest} from a knock", item.Destination);
        if (item.Destination == InviteDestination.Ramp) OpenPanelTab("studio");
        if (item.Destination == InviteDestination.Chess) GameWindow.JoinPbpChallenge(item.Code!);
        if (item.Destination == InviteDestination.Breakout && CanJoin(item))
        {
            try { LaunchGame(BreakoutFullAllowed ? "breakout" : "breakoutdemo"); }
            catch (Exception ex) { Log.Warning(ex, "[Friends] breakout launch failed"); }
        }
    }

    internal static Func<string?, bool> OpenRace { get; set; } = room =>
    {
        try { return GameWindow.LaunchFriendRace(room) != null; }
        catch (Exception ex) { Log.Warning(ex, "[Friends] race launch failed"); return false; }
    };

    internal static FriendsSfxGate SfxGate = new();
    // WPF FriendsSfx.cs:23-24, through the existing master-volume and gap gate.
    internal static void DrawerOpen() => Sfx("chaos/cards_in.mp3", 0.14f, "friends-open");
    internal static void DrawerClose() => Sfx("chaos/ui_unequip.mp3", 0.10f, "friends-close");

    /// <summary>The gap rules' clock; tests step it.</summary>
    internal static Func<DateTime> SfxNow = () => DateTime.UtcNow;

    // WPF FriendsLanding.cs:632-696: resolve a reference locally; never trust a sender's URL.
    internal static void Watch(WatchRef watch)
    {
        if (!watch.IsValid()) return;
        switch (watch.Kind)
        {
            case WatchKind.Catalogue:
                string? path = null;
                try
                {
                    foreach (var entry in CoreSettings.Current.DeeperSubmissions)
                        if (string.Equals(entry.Value?.CatalogueId, watch.Id, StringComparison.Ordinal) && File.Exists(entry.Key))
                        { path = entry.Key; break; }
                }
                catch (Exception ex) { Log.Debug(ex, "[Friends] catalogue lookup failed"); }
                if (path == null) { OpenPanelTab("deeper"); return; }
                try { PlayWatchCatalogue(path); }
                catch (Exception ex) { Log.Warning(ex, "[Friends] deeper player failed"); OpenPanelTab("deeper"); }
                return;
            case WatchKind.Flavour:
                var s = CoreSettings.Current;
                var niches = LandingRules.FlavourNiches(watch.Id);
                if (niches.Count == 0) return;
                try
                {
                    var source = LauncherMediaSettings.IsKnownSource(s.MediaSource) ? s.MediaSource : LauncherMediaSettings.SourceLocal;
                    if (source == LauncherMediaSettings.SourceLocal && s.HasRemoteMediaConsent) source = LauncherMediaSettings.SourceMixed;
                    LauncherMediaSettings.Apply(s, source, s.RemoteMediaRatio, niches.ToList());
                    FypOnlineCoordinator.ResetAllChannels();
                    // WPF FriendsLanding.cs:683-685: discard the dealt pools (flash, mandatory video, bubble
                    // count), then save. The head owns these schedulers, so this works without a shell too.
                    Views.Tabs.AssetsTabView.InvalidateAssetPoolsAfterSelectionChange();
                    var anchor = ActiveGameWindow() ?? Anchor();
                    if (anchor != null) ShowWatchWord(anchor, Str("friends_land_flavour_on", "flavour on"));
                }
                catch (Exception ex) { Log.Warning(ex, "[Friends] flavour apply failed"); }
                return;
            case WatchKind.Ht:
                var url = LandingRules.HtUrl(watch.Id);
                if (url != null)
                {
                    try { OpenWatchUrl(url); }
                    catch (Exception ex) { Log.Warning(ex, "[Friends] ht watch failed"); }
                }
                return;
        }
    }

    // Small head seams for the WPF player and browser calls; tests replace the window edges.
    internal static Action<Window, string> ShowWatchWord { get; set; } = (owner, text) => FloatingWord.Throw(owner, text, pink: true, small: true);
    internal static Action<string> PlayWatchCatalogue { get; set; } = path =>
    {
        var panel = MainShellWindow.Current ?? throw new InvalidOperationException("Panel unavailable");
        panel.ShowOrActivateDeeperPlayer().LoadEnhancementFile(path);
    };
    internal static Action<string> OpenWatchUrl { get; set; } = url =>
    {
        if (MainShellWindow.Current is not { } panel) return;
        LauncherWindow.OpenPanel(panel);
        panel.NavigateToUrlInBrowser(url, autoPlayFullscreen: false, userInitiated: true);
    };


    /// <summary>WPF FriendsSfx.Play: master volume, the 1.5 s incoming gap, half level in a session.</summary>
    internal static void Sfx(string rel, float scale, string tag, bool incoming = false)
    {
        try
        {
            var s = CoreSettings.Current;
            // WPF Services/Friends/FriendsSfx.cs:47-51: silence under output suppression, video or Emi hold.
            if (s.MasterVolume <= 0 || CoreAudio.IsOutputSuppressed || CoreEngine.Video?.IsPlaying == true
                || ConditioningControlPanel.Services.EmiDesk.EmiLineEngine.Instance.HoldActive) return;
            lock (SfxGate) if (!SfxGate.TryPass(SfxNow(), incoming)) return;
            var path = CoreModArt.AudioOverridePath(rel)
                ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var level = s.MasterVolume / 100f * scale * FriendsSfxGate.Level(incoming, CoreSession.IsSessionRunning);
            CoreAudio.PlayOneShot(path, Math.Clamp(level, 0f, 1f), tag);
        }
        catch (Exception ex) { Log.Debug(ex, "[Friends] sfx {Tag} failed", tag); }
    }
}
