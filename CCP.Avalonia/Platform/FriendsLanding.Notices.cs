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

    internal static Action<string?> OpenDrawer { get; set; } = id =>
    {
        if (MainShellWindow.Current is not { } panel) return;
        LauncherWindow.OpenPanel(panel);
        Dispatcher.UIThread.Post(() => panel.FindControl<FriendsRailChip>("FriendsChip")?.OpenDrawer(id), DispatcherPriority.ApplicationIdle);
    };

    private static void ShowPoke(InboxItem item, Window? anchor)
    {
        Sfx("chaos/dling.mp3", 0.14f, "friends-poke", true);
        FriendNotices.Present(anchor, Notice(NoticeKind.Poke, item.FromId, item.FromName, item, item.At), new NoticeLook
        {
            Line = Str("friends_notice_poked", "poked you:"),
            Word = Str("friends_poke_" + item.PokeId, LandingRules.PokeFallback(item.PokeId)),
            Pink = LandingRules.PokeIsPink(item.PokeId), AvatarUrl = item.FromAvatarUrl,
            ActionLabel = Str("friends_notice_poke_back", "Poke back"),
            Act = async p =>
            {
                if (p is not InboxItem i || Service() is not { } svc) return;
                try
                {
                    var result = await svc.PokeAsync(i.FromId, string.IsNullOrEmpty(i.PokeId) ? "hi" : i.PokeId);
                    if (!FriendsDrawerRules.IsGood(result)) Tell(Loc.Get(FriendsDrawerRules.SendResultKey(result)), false);
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
        Sfx("chaos/dling.mp3", 0.16f, "friends-request", true);
        FriendNotices.Present(anchor, Notice(NoticeKind.Request, request.Id, request.Name, request, request.At), new NoticeLook
        {
            Line = Str("friends_land_request_line", "wants to be friends"), AvatarUrl = request.AvatarUrl,
            ActionLabel = Str("friends_notice_accept", "Accept"), Open = _ => OpenDrawer(null),
            Act = async p =>
            {
                if (p is not FriendRequest r || Service() is not { } svc) return;
                try
                {
                    var result = await svc.AcceptAsync(r.Id);
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
        FriendNotices.Present(anchor, Notice(NoticeKind.Request, key, waiting.Count == 1 ? newest.Name : "", newest, newest.At), new NoticeLook
        {
            Line = waiting.Count == 1 ? summary : title, ActionLabel = Str("friends_land_waiting_open", "Open"),
            Act = _ => OpenDrawer(null), Open = _ => OpenDrawer(null),
        });
        if (waiting.Count == 1) FriendsSeen.Shared.RequestSeen(Service(), newest);
    }
}
