using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using RowItem = ConditioningControlPanel.Services.Startup.InboxItem;
namespace ConditioningControlPanel.Avalonia.Platform;
internal static partial class FriendsLanding
{
    private static FriendNotice Notice(NoticeKind kind, string id, string name, object payload, DateTimeOffset at) =>
        new(kind, id, name, LandingRules.NoticeAt(at, DateTimeOffset.UtcNow), FriendNoticeRules.LifetimeMs(kind), payload);
    internal static Action<string?> OpenDrawer { get; set; } = id => OpenDrawerIn(MainShellWindow.Current, id);
    /// <summary>WPF Services/Friends/FriendsLanding.cs:457: the panel up, then (after a layout pass) the rail chip's drawer on the friend.</summary>
    internal static void OpenDrawerIn(MainShellWindow? panel, string? id)
    {
        if (panel == null) return;
        void Shown(MainShellWindow shown) => Dispatcher.UIThread.Post(
            () => shown.FindControl<FriendsRailChip>("FriendsChip")?.OpenDrawer(id), DispatcherPriority.ApplicationIdle);
        if (LauncherWindow.Instance is { } launcher) launcher.OpenPanel(Shown);
        else { LauncherWindow.OpenPanel(panel); Shown(panel); }
    }
    // WPF Services/Friends/FriendsLanding.cs:230-257: silent bell, game notice, missed Inbox.
    internal static void AnnouncePoke(InboxItem item, bool inGame)
    {
        if (!NoticesOn) { TheSink.Inbox(item); return; }
        var word = Str("friends_poke_" + item.PokeId, LandingRules.PokeFallback(item.PokeId));
        var pink = LandingRules.PokeIsPink(item.PokeId);
        Sfx("bubbles/Pop3.mp3", inGame ? 0.10f : 0.16f, "friends-poke-in", true);
        var game = inGame ? ActiveGameWindow() : null;
        FriendNotices.Present(game ?? Anchor(), Notice(NoticeKind.Poke, item.FromId, item.FromName, item, item.At), new NoticeLook
        {
            Line = Str("friends_notice_poked", "poked you:"), Word = word, Pink = pink,
            AvatarUrl = item.FromAvatarUrl, ActionLabel = Str("friends_notice_poke_back", "Poke back"),
            Act = async p => { if (p is InboxItem i) await PokeBack(i); },
            Open = p => OpenDrawer((p as InboxItem)?.FromId),
            Missed = p => { if (p is InboxItem i) TheSink.Inbox(i); },
        });
        FriendsSeen.Shared.Seen(Service(), item.Id);
        if (game != null) return;
        EmiSays(string.Format(Str("friends_land_emi_poke", "{0} says {1}"), item.FromName, word),
            LandingRules.PokeFace(item.PokeId));
        if (Anchor() is { } anchor) FloatingWord.Throw(anchor, word, pink);
    }
    // WPF Services/Friends/FriendsLanding.cs:419-433: same poke back, fallback hi, refusal only.
    internal static async System.Threading.Tasks.Task PokeBack(InboxItem item)
    {
        try
        {
            if (Service() is not { } svc || string.IsNullOrEmpty(item.FromId)) return;
            SfxClick();
            var result = await svc.PokeAsync(item.FromId, string.IsNullOrEmpty(item.PokeId) ? "hi" : item.PokeId);
            if (FriendsDrawerRules.IsGood(result)) return;
            SfxDenied();
            Tell(Loc.Get(FriendsDrawerRules.SendResultKey(result)), false);
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Friends poke back"); }
    }
    // WPF FriendsSfx (Services/Friends/FriendsSfx.cs:25-39).
    private static void SfxClick() => Sfx("chaos/ui_click.mp3", 0.12f, "friends-click");
    private static void SfxDenied() => Sfx("chaos/ui_denied.mp3", 0.14f, "friends-denied");
    private static void SfxRequest() => Sfx("chaos/dling.mp3", 0.16f, "friends-request", true);
    // WPF Services/Friends/FriendsLanding.cs:311-382,436-461: requests, waiting and drawer.
    internal static void FileRequest(FriendRequest request) => StartupLadder.Inbox.File(new RowItem
    {
        Key = "friends-request:" + request.Id, Title = request.Name, Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.FriendRequest,
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
        StartupLadder.Inbox.File(new RowItem { Key = key, Title = title, Summary = summary, Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.FriendRequest, Open = () => OpenDrawer(null) });
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
}
