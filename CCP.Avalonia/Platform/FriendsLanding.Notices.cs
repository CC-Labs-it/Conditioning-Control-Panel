using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Launcher;
using Serilog;
using RowItem = ConditioningControlPanel.Services.Startup.InboxItem;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static partial class FriendsLanding
{
    private static FriendNotice Notice(NoticeKind kind, string id, string name, object payload, DateTimeOffset at) =>
        new(kind, id, name, LandingRules.NoticeAt(at, DateTimeOffset.UtcNow), FriendNoticeRules.LifetimeMs(kind), payload);

    internal static Action<string?> OpenDrawer { get; set; } = id => OpenDrawerIn(MainShellWindow.Current, id);

    /// <summary>WPF OpenDrawer :455: the panel up, then (after a layout pass) the rail chip's drawer on the friend.</summary>
    internal static void OpenDrawerIn(MainShellWindow? panel, string? id)
    {
        if (panel == null) return;
        LauncherWindow.OpenPanel(panel);
        Dispatcher.UIThread.Post(() => panel.FindControl<FriendsRailChip>("FriendsChip")?.OpenDrawer(id), DispatcherPriority.ApplicationIdle);
    }

    // WPF FriendsSfx (Services/Friends/FriendsSfx.cs:25-39).
    private static void SfxClick() => Sfx("chaos/ui_click.mp3", 0.12f, "friends-click");
    private static void SfxDenied() => Sfx("chaos/ui_denied.mp3", 0.14f, "friends-denied");
    private static void SfxRequest() => Sfx("chaos/dling.mp3", 0.16f, "friends-request", true);

    private static void ShowPoke(InboxItem item, Window? anchor, bool inGame)
    {
        Sfx("bubbles/Pop3.mp3", inGame ? 0.10f : 0.16f, "friends-poke-in", true);   // WPF FriendsSfx.PokeIn
        FriendNotices.Present(anchor, Notice(NoticeKind.Poke, item.FromId, item.FromName, item, item.At), new NoticeLook
        {
            Line = Str("friends_notice_poked", "poked you:"),
            Word = Str("friends_poke_" + item.PokeId, LandingRules.PokeFallback(item.PokeId)),
            Pink = LandingRules.PokeIsPink(item.PokeId), AvatarUrl = item.FromAvatarUrl,
            ActionLabel = Str("friends_notice_poke_back", "Poke back"),
            Act = async p =>
            {
                if (p is not InboxItem i || Service() is not { } svc || string.IsNullOrEmpty(i.FromId)) return;
                try
                {
                    SfxClick();
                    var result = await svc.PokeAsync(i.FromId, string.IsNullOrEmpty(i.PokeId) ? "hi" : i.PokeId);
                    if (FriendsDrawerRules.IsGood(result)) return;
                    SfxDenied();
                    Tell(Loc.Get(FriendsDrawerRules.SendResultKey(result)), false);
                }
                catch (Exception ex) { Serilog.Log.Debug(ex, "Friends poke back"); }
            },
            Open = p => OpenDrawer((p as InboxItem)?.FromId),
            Missed = p => { if (p is InboxItem i) TheSink.Inbox(i); },
        });
    }

    internal static void FileRequest(FriendRequest request) => StartupLadder.Inbox.File(new RowItem
    {
        Key = "friends-request:" + request.Id, Title = request.Name,
        Summary = Str("friends_land_request_line", "wants to be friends"),
        Open = () => OpenDrawer(null),
    });

    internal static void AnnounceRequest(FriendRequest request, bool inGame)
    {
        var anchor = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
        if (!NoticesOn || anchor == null) return;
        SfxRequest();
        FriendNotices.Present(anchor, Notice(NoticeKind.Request, request.Id, request.Name, request, request.At), new NoticeLook
        {
            Line = Str("friends_land_request_line", "wants to be friends"), AvatarUrl = request.AvatarUrl,
            ActionLabel = Str("friends_notice_accept", "Accept"), Open = _ => OpenDrawer(null),
            Act = async p =>
            {
                if (p is not FriendRequest r || Service() is not { } svc) return;
                try
                {
                    SfxClick();
                    var result = await svc.AcceptAsync(r.Id);
                    if (result == ActResult.Done) Sfx("chaos/ui_unlock.mp3", 0.18f, "friends-accepted"); else SfxDenied();
                    Tell(Loc.Get(result == ActResult.Done ? "friends_add_accepted" : FriendsDrawerRules.ActResultKey(result)), result == ActResult.Done);
                }
                catch (Exception ex) { Serilog.Log.Debug(ex, "Friends accept"); }
            },
        });
        FriendsSeen.Shared.RequestSeen(Service(), request);
    }

    internal static void Waiting(IReadOnlyList<FriendRequest> waiting, bool announce, bool inGame)
    {
        const string key = "friends-requests-waiting";
        if (waiting.Count == 0) { StartupLadder.Inbox.Remove(key); return; }
        var newest = waiting[0];
        foreach (var r in waiting) if (r.At > newest.At) newest = r;
        var title = waiting.Count == 1 ? newest.Name : string.Format(Str("friends_land_waiting_many", "{0} friend requests waiting"), waiting.Count);
        var summary = waiting.Count == 1 ? Str("friends_land_request_line", "wants to be friends") : "";
        StartupLadder.Inbox.Remove(key);
        StartupLadder.Inbox.File(new RowItem { Key = key, Title = title, Summary = summary, Open = () => OpenDrawer(null) });
        var anchor = inGame ? ActiveGameWindow() ?? Anchor() : Anchor();
        if (!announce || !NoticesOn || anchor == null) return;
        SfxRequest();
        FriendNotices.Present(anchor, Notice(NoticeKind.Request, key, waiting.Count == 1 ? newest.Name : "", newest, newest.At), new NoticeLook
        {
            Line = waiting.Count == 1 ? summary : title, ActionLabel = Str("friends_land_waiting_open", "Open"),
            Act = _ => OpenDrawer(null), Open = _ => OpenDrawer(null),
        });
        if (waiting.Count == 1) FriendsSeen.Shared.RequestSeen(Service(), newest);
    }

    /// <summary>WPF Sink.Knock :280: a watch knocks as a corner notice with Watch / Try it.</summary>
    private static void ShowWatch(Window anchor, InboxItem item)
    {
        FriendNotices.Present(anchor, Notice(NoticeKind.Watch, item.FromId, item.FromName, item, item.At), new NoticeLook
        {
            Line = KnockLine(item), AvatarUrl = item.FromAvatarUrl,
            ActionLabel = item.Watch?.Kind == WatchKind.Flavour ? Str("friends_land_try", "Try it") : Str("friends_land_watch", "Watch"),
            Act = p => { if (p is InboxItem i && !i.IsExpired(KnockCard.Clock())) OnKnockDone(i, KnockOutcome.Go); },
            Open = p => OpenDrawer((p as InboxItem)?.FromId),
            // Still answerable after the toast goes: it waits in the Inbox, where it reopens as a notice.
            Left = p => { if (p is InboxItem i && !i.IsExpired(KnockCard.Clock())) TheSink.Inbox(i); },
        });
        FriendsSeen.Shared.Seen(Service(), item.Id);
    }

    /// <summary>The Deeper player on a file (MainShellWindow.PlayDeeperLibraryEntry); tests replace it.</summary>
    internal static Action<string> PlayDeeper { get; set; } = path => MainShellWindow.Current?.PlayDeeperLibraryEntry(path);

    /// <summary>WPF WatchHt :684: the panel up, then the embedded browser on the URL; tests replace it.</summary>
    internal static Action<string> OpenInBrowser { get; set; } = url =>
    {
        if (MainShellWindow.Current is not { } mw) return;
        LauncherWindow.OpenPanel(mw);
        mw.NavigateToUrlInBrowser(url, autoPlayFullscreen: false, userInitiated: true);
    };

    /// <summary>WPF Watch :622.</summary>
    internal static void Watch(WatchRef watch)
    {
        if (!watch.IsValid()) return;
        Log.Information("[Friends] watch {Kind} from a knock", watch.Kind);
        try
        {
            switch (watch.Kind)
            {
                case WatchKind.Catalogue:
                    // WPF WatchCatalogue :634: the player on the local file that carries this catalogue id, else the tab.
                    string? path = null;
                    foreach (var kv in CoreSettings.Current.DeeperSubmissions ?? new())
                        if (string.Equals(kv.Value?.CatalogueId, watch.Id, StringComparison.Ordinal) && File.Exists(kv.Key)) { path = kv.Key; break; }
                    if (path != null) PlayDeeper(path); else OpenPanelTab("deeper");
                    return;
                case WatchKind.Flavour: WatchFlavour(watch.Id); return;
                case WatchKind.Ht:
                    if (LandingRules.HtUrl(watch.Id) is { } url) OpenInBrowser(url);
                    return;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[Friends] watch {Kind} failed", watch.Kind); }
    }

    /// <summary>WPF WatchFlavour :657: the launcher's own apply path. A local-only player keeps their source:
    /// pulling from Reddit is a consent the panel asks for, never one a friend's watch grants.</summary>
    private static void WatchFlavour(string flavour)
    {
        var s = CoreSettings.Current;
        var niches = LandingRules.FlavourNiches(flavour);
        if (niches.Count == 0) return;
        var source = s.MediaSource;
        if (!LauncherMediaSettings.IsKnownSource(source)) source = LauncherMediaSettings.SourceLocal;
        if (source == LauncherMediaSettings.SourceLocal && s.HasRemoteMediaConsent) source = LauncherMediaSettings.SourceMixed;
        LauncherMediaSettings.Apply(s, source, s.RemoteMediaRatio, niches.ToList());
        FypOnlineCoordinator.ResetAllChannels();
        CoreSettings.Save();   // WPF InvalidateAssetPools has no pool on this head (LauncherMediaDialog.cs:377)
        var anchor = ActiveGameWindow() ?? Anchor();
        if (anchor != null) FloatingWord.Throw(anchor, Str("friends_land_flavour_on", "flavour on"), pink: true, small: true);
    }
}
