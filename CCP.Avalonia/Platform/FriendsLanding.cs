using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Serilog;
using RowItem = ConditioningControlPanel.Services.Startup.InboxItem;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// PORTED (invites only) from ConditioningControlPanel/Services/Friends/FriendsLanding.cs: what a delivery
/// does when it lands. Core's <see cref="FriendsLandingRouter"/> decides (hold, Inbox, knock); this sink
/// knocks an invite with a <see cref="KnockCard"/> and files Inbox rows. Nothing here plays or joins by
/// itself. A light timer re-attaches to <see cref="FriendsHead.Service"/> and releases the held queue (WPF :28-82).
/// ponytail: the corner notices (FriendNotices: poke toast, watch toast, request toast) are not on this head
/// (row feat-friends-open-tables), so a poke and a watch go to the Inbox as WPF does with the bell off
/// (WPF :212); request rows/cues, SentBeat words and Emi lines wait for row feat-friends-landing.
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
    }

    /// <summary>One timer tick (WPF Tick :48): follow the current service, release the held queue, and
    /// fold any card still up when a hold (lockdown, Strict Lock, a session) starts.</summary>
    internal static void Tick()
    {
        try
        {
            var current = FriendsHead.Service;
            if (current != null) ConditioningControlPanel.Services.Friends.FriendsLocalBlocks.MigrateIfDue(current);
            if (!ReferenceEquals(_router?.Service, current))
            {
                _router?.Dispose();
                // The server's clock: an invite's at / expires_at are server times.
                _router = current == null ? null : new FriendsLandingRouter(current, () => World(), () => KnockCard.Clock(), TheSink);
            }
            _router?.Release();
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
            // ponytail: GameWindow (Breakout demo) is not a WPF landing host; ActiveGameWindow waits for the hosts.
            GameHostActive: false,
            SessionRunning: session);
    }

    private static Window? DefaultAnchor()
    {
        var mw = MainShellWindow.Current;
        if (mw is { IsVisible: true } && mw.WindowState != WindowState.Minimized) return mw;
        var lw = LauncherWindow.Instance;
        return lw is { IsVisible: true } && lw.WindowState != WindowState.Minimized ? lw : null;
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
            OnKnockDone, shown: i => FriendsSeen.Shared.Seen(FriendsHead.Service, i.Id));

    private sealed class Sink : ILandingSink
    {
        public void Poke(InboxItem item, bool inGame) => Inbox(item);

        public void Knock(InboxItem item, bool inGame)
        {
            var anchor = Anchor();
            // The bell off, or nothing of ours on screen: the Inbox row and nothing else (WPF :227-229).
            if (anchor == null || !NoticesOn || item.Kind != SendKind.Invite) { Inbox(item); return; }
            Sfx("chaos/dling.mp3", 0.18f, "friends-knock", incoming: true);   // WPF FriendsSfx.Knock
            // An invite knocks with a card that counts down to its own expiry (WPF :251-258).
            ShowCard(anchor, item);
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

        public void SentBeat(SendKind kind, Friend to) { }
        public void RequestRow(FriendRequest request) { }
        public void RequestAnnounce(FriendRequest request, bool inGame) { }
        public void RequestCue() { }
        public void RequestGone(string requestId) { }
        public void RequestsWaiting(IReadOnlyList<FriendRequest> waiting, bool announce, bool inGame) { }
    }

    /// <summary>The drawer's bell (WPF NoticesOn :348). Off: Inbox rows only.</summary>
    private static bool NoticesOn => CoreSettings.Current.FriendNotificationsEnabled;

    /// <summary>An Inbox row opened later: the card again while it is still answerable (WPF Reopen :465).
    /// Opened by hand, so it comes up whatever the bell says.</summary>
    private static void Reopen(InboxItem item)
    {
        if (item.IsExpired(KnockCard.Clock())) return;   // ponytail: WPF's Emi "that one already left" line
        if (item.Kind == SendKind.Invite)
        {
            if (Anchor() is { } anchor) ShowCard(anchor, item);
            else TheSink.Inbox(item);   // WPF :478 Knock -> no anchor -> the row again
        }
        // ponytail: a poke/watch row reopens as its corner notice in WPF (FriendNotices, not on this head).
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
                FriendsSeen.Shared.Report(FriendsHead.Service, ReceiptReport.Item(item.Id, ReceiptState.Declined));
                StartupLadder.Inbox.Remove("friends:" + item.Id);
                Sfx("chaos/sink.mp3", 0.10f, "friends-sink");   // WPF FriendsSfx.Dismiss
                return;
        }
        if (item.Kind != SendKind.Invite) { StartupLadder.Inbox.Remove("friends:" + item.Id); return; }
        if (!CanJoin(item)) { OnKnockDone(item, KnockOutcome.Later); return; }   // never a Joined receipt for nothing
        StartupLadder.Inbox.Remove("friends:" + item.Id);
        FriendsSeen.Shared.Report(FriendsHead.Service, ReceiptReport.Item(item.Id, ReceiptState.Joined));
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

    /// <summary>Not readonly: a test swaps in a fresh gate so two cues inside the 130 ms gap both play.</summary>
    internal static FriendsSfxGate SfxGate = new();

    /// <summary>WPF FriendsSfx.Click / Sent / Accepted / Dismiss / Denied: the drawer's own cues (FriendsSfx.cs:26-31).</summary>
    internal static void SfxClick() => Sfx("chaos/ui_click.mp3", 0.12f, "friends-click");
    internal static void SfxSent() => Sfx("chaos/chip_pop.mp3", 0.16f, "friends-sent");
    internal static void SfxAccepted() => Sfx("chaos/ui_unlock.mp3", 0.18f, "friends-accepted");
    internal static void SfxDismiss() => Sfx("chaos/sink.mp3", 0.10f, "friends-sink");
    internal static void SfxDenied() => Sfx("chaos/ui_denied.mp3", 0.14f, "friends-denied");

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
