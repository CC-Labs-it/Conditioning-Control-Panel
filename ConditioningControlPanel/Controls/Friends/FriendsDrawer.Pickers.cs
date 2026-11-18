using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The three pickers that open inside a friend's card, and the menu under "more". Pickers are
/// drawn INLINE in the card rather than as popups of their own: the drawer is already a popup,
/// and a popup inside a popup is where WPF's click-outside logic starts losing arguments.
/// </summary>
public sealed partial class FriendsDrawer
{
    private FrameworkElement BuildPicker(Friend f, string picker) => picker switch
    {
        "poke" => BuildPokePicker(f),
        "invite" => BuildInvitePicker(f),
        _ => BuildWatchPicker(f),
    };

    // ---- poke -------------------------------------------------------------------------

    private FrameworkElement BuildPokePicker(Friend f)
    {
        // Word stamps: light rectangles on a poke-pink edge, in the tray the card labels (no ovals).
        var wrap = new WrapPanel { Tag = "friends-picker:poke", Margin = new Thickness(0, 0, -8, -8) };
        foreach (var id in PokeSet.Shipped)
        {
            Button chip = null!;
            chip = DareLook.Choice(Loc.Get("friends_poke_" + id), null, () =>
            {
                Pop(chip, FriendsLook.Pink);
                _ = PokeAsync(f.Id, id);
            }, "friends-poke:" + id, compact: true, edge: DareLook.ToneColor(DareTone.Video));
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    /// <summary>Pokes a friend and words the answer in the row. Internal for the suite.</summary>
    internal async Task<SendResult> PokeAsync(string friendId, string pokeId)
    {
        if (_svc == null || !PokeSet.IsValid(pokeId)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.PokeAsync(friendId, pokeId); }
        catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    // ---- invite -----------------------------------------------------------------------

    /// <summary>The invite tiles, one per <see cref="InviteDestination.Sendable"/>. Remote is gone
    /// (owner, 2026-09-28): its PIN never travelled, so a Remote invite was a dead end.</summary>
    internal static readonly (string Id, string Art)[] InviteTiles =
    {
        (InviteDestination.Goon, "features/goon_game_tile.png"),
        (InviteDestination.BackRoom, "features/backroom.png"),
        (InviteDestination.Ramp, "features/Phrase_Lock.png"),
        (InviteDestination.Chess, "features/piecebypiece.png"),
    };

    private FrameworkElement BuildInvitePicker(Friend f)
    {
        var grid = new UniformGrid { Columns = 2, Tag = "friends-picker:invite", Margin = new Thickness(0, 0, 0, -6) };
        foreach (var (id, art) in InviteTiles)
        {
            var (code, blockedKey) = InviteCodes.For(id);
            var tile = InviteTile(id, art, blockedKey != null);
            tile.Margin = new Thickness(grid.Children.Count % 2 == 0 ? 0 : 3, 0, grid.Children.Count % 2 == 0 ? 3 : 0, 6);
            if (blockedKey != null)
            {
                tile.IsEnabled = false;
                // A disabled button shows no tooltip unless it is told to.
                ToolTipService.SetShowOnDisabled(tile, true);
                tile.ToolTip = Loc.Get(blockedKey);
            }
            else if (id == InviteDestination.Goon && string.IsNullOrEmpty(code))
            {
                tile.ToolTip = Loc.Get("friends_invite_goon_tip");
            }
            tile.Click += async (_, _) =>
            {
                Pop(tile, FriendsLook.Lilac);
                if (id == InviteDestination.Goon) await InviteToGoonAsync(f.Id);
                else if (id == InviteDestination.Chess) await InviteToChessAsync(f.Id);
                else await InviteAsync(f.Id, id, code);
            };
            grid.Children.Add(tile);
        }
        return grid;
    }

    /// <summary>A game tile in the card's family: the picture inset on a light face, the name in
    /// print under it; a tile this account cannot use wears a padlock block (locked stays visible).</summary>
    private static Button InviteTile(string id, string art, bool locked)
    {
        var sp = new StackPanel();
        var pic = FriendsLook.Art(art, 256);
        if (pic != null)
        {
            var img = new Image { Source = pic, Stretch = Stretch.UniformToFill, Height = 58 };
            var holder = new Grid { ClipToBounds = true, Height = 58 };
            holder.SizeChanged += (_, e) => holder.Clip = new RectangleGeometry(new Rect(e.NewSize), 4, 4);
            holder.Children.Add(img);
            if (locked)
            {
                var badge = DareLook.Block(DareLook.Icon("lock", 15, DareLook.WhiteBrush, null, 2), DareLook.Ink, 24);
                badge.HorizontalAlignment = HorizontalAlignment.Right;
                badge.VerticalAlignment = VerticalAlignment.Top;
                badge.Margin = new Thickness(0, 4, 4, 0);
                holder.Children.Add(badge);
            }
            sp.Children.Add(holder);
        }
        var name = Loc.Get("friends_invite_" + id);
        var t = DareLook.Words(name, 15, DareLook.CardInkBrush, weight: FontWeights.Bold);
        t.Margin = new Thickness(3, 4, 3, 1);
        sp.Children.Add(t);
        return DareLook.PressTile(sp, DareLook.ToneColor(DareTone.Minutes), false, "friends-invite:" + id, name, new Thickness(4));
    }

    private bool _openingGoonRoom;

    /// <summary>The Goon tile: send the live room code, or open a room first and send its code
    /// the moment the game reports it. One tap either way. Internal for the suite.</summary>
    internal async Task<SendResult?> InviteToGoonAsync(string friendId)
    {
        var code = InviteCodes.GoonCode();
        if (string.IsNullOrEmpty(code))
        {
            if (!InviteCodes.CanHostGoon())
            {
                ShowNote(friendId, "friends_invite_goon_prime", good: false, timed: true);
                return null;
            }
            if (_openingGoonRoom) return null;   // a second tap while the room opens costs nothing
            _openingGoonRoom = true;
            ShowNote(friendId, "friends_invite_goon_opening");
            (string? Code, bool Busy) opened;
            try { opened = await InviteCodes.OpenGoonRoom(TimeSpan.FromSeconds(45)); }
            catch { opened = (null, false); }
            finally { _openingGoonRoom = false; }
            if (string.IsNullOrEmpty(opened.Code))
            {
                ShowNote(friendId, opened.Busy ? "friends_invite_goon_busy" : "friends_invite_goon_failed",
                    good: false, timed: true);
                return null;
            }
            code = opened.Code;
        }
        return await InviteAsync(friendId, InviteDestination.Goon, code);
    }

    private bool _openingChess;

    /// <summary>The chess tile: open the board on a challenge to this friend, then send the
    /// challenge id as the invite. One tap. Internal for the suite.</summary>
    internal async Task<SendResult?> InviteToChessAsync(string friendId)
    {
        if (_openingChess) return null;   // a second tap while the board opens costs nothing
        _openingChess = true;
        ShowNote(friendId, "friends_invite_chess_opening");
        string? challenge;
        try { challenge = await InviteCodes.ChallengeFriend(friendId, TimeSpan.FromSeconds(45)); }
        catch { challenge = null; }
        finally { _openingChess = false; }
        if (!InviteDestination.IsChallengeId(challenge))
        {
            ShowNote(friendId, "friends_invite_chess_failed", good: false, timed: true);
            return null;
        }
        return await InviteAsync(friendId, InviteDestination.Chess, challenge);
    }

    internal async Task<SendResult> InviteAsync(string friendId, string destination, string? code)
    {
        if (_svc == null || !InviteDestination.IsSendable(destination)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.InviteAsync(friendId, destination, code); }
        catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    // ---- watch ------------------------------------------------------------------------

    private static readonly string[] WatchTabs = { "catalogue", "flavour", "ht" };

    private FrameworkElement BuildWatchPicker(Friend f)
    {
        var sp = new StackPanel { Tag = "friends-picker:watch" };
        // The three sources as word stamps on the watch blue; the one showing is pressed in.
        var tabs = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var tab in WatchTabs)
        {
            var b = DareLook.Choice(Loc.Get("friends_watch_tab_" + tab), null, () => { _watchTab = tab; Render(); },
                "friends-watch-tab:" + tab, compact: true, edge: DareLook.ToneColor(DareTone.Bubbles), on: _watchTab == tab);
            b.Margin = new Thickness(tabs.Children.Count == 0 ? 0 : 3, 0, tabs.Children.Count == WatchTabs.Length - 1 ? 0 : 3, 6);
            tabs.Children.Add(b);
        }
        sp.Children.Add(tabs);

        switch (_watchTab)
        {
            case "catalogue":
                sp.Children.Add(CatalogueList(f));
                break;
            case "ht":
                sp.Children.Add(HtBox(f));
                break;
            default:
                var wrap = new WrapPanel { Margin = new Thickness(0, 0, -8, -8) };
                foreach (var fl in WatchRef.Flavours)
                {
                    var title = Loc.Get("friends_flavour_" + fl);
                    Button chip = null!;
                    chip = DareLook.Choice(title, null, () =>
                    {
                        Pop(chip, FriendsLook.Gold);
                        _ = SendWatchAsync(f.Id, new WatchRef(WatchKind.Flavour, fl, title));
                    }, "friends-flavour:" + fl, compact: true, edge: DareLook.Gold);
                    wrap.Children.Add(chip);
                }
                sp.Children.Add(wrap);
                break;
        }
        return sp;
    }

    private FrameworkElement CatalogueList(Friend f)
    {
        var items = CatalogueWatches.List();
        if (items.Count == 0)
        {
            var t = FriendsLook.Label(Loc.Get("friends_watch_catalogue_empty"), 12, FriendsLook.MutedBrush);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            t.Tag = "friends-watch-catalogue-empty";
            return t;
        }
        var sp = new StackPanel();
        foreach (var (id, title) in items)
        {
            Button b = null!;
            b = DareLook.Choice(title, null, () =>
            {
                Pop(b, FriendsLook.Gold);
                _ = SendWatchAsync(f.Id, new WatchRef(WatchKind.Catalogue, id, title));
            }, "friends-watch-catalogue:" + id, compact: true, edge: DareLook.Gold);
            b.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(b);
        }
        return sp;
    }

    private FrameworkElement HtBox(Friend f)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            FontFamily = FriendsLook.Mono,
            FontSize = 13,
            Foreground = FriendsLook.TextBrush,
            Background = FriendsLook.Frozen(FriendsLook.Ground),
            BorderBrush = FriendsLook.Line2Brush,
            CaretBrush = FriendsLook.LilacBrush,
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = Loc.Get("friends_watch_ht_hint"),
            Tag = "friends-ht-box",
        };
        Button send = null!;
        send = DareLook.Choice(Loc.Get("friends_watch_send"), null, () =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id.Length == 0) return;
            Pop(send, FriendsLook.Gold);
            _ = SendWatchAsync(f.Id, new WatchRef(WatchKind.Ht, id, null));
        }, "friends-ht-send", compact: true, edge: DareLook.Gold);
        send.Margin = new Thickness(6, 0, 0, 0);
        send.IsEnabled = false;
        box.TextChanged += (_, _) =>
        {
            var n = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (n != box.Text) { box.Text = n; box.CaretIndex = n.Length; }
            send.IsEnabled = n.Length > 0;
        };
        g.Children.Add(box);
        Grid.SetColumn(send, 1);
        g.Children.Add(send);
        return g;
    }

    internal async Task<SendResult> SendWatchAsync(string friendId, WatchRef watch)
    {
        if (_svc == null || !watch.IsValid()) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.SendWatchAsync(friendId, watch); }
        catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    // ---- the menu (right-click and "more") --------------------------------------------

    private ContextMenu BuildMenu(Friend f)
    {
        var menu = new ContextMenu
        {
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x22, 0x16, 0x41)),
            BorderBrush = FriendsLook.Line2Brush,
            Foreground = FriendsLook.TextBrush,
            FontFamily = FriendsLook.Display,
            FontSize = 13.5,
            Tag = "friends-menu:" + f.Id,
        };
        foreach (var item in FriendsDrawerRules.MenuItems(f.Squelched, DaresOpen()))
        {
            if (item == null) { menu.Items.Add(new Separator { Background = FriendsLook.LineBrush }); continue; }
            var mi = MenuItem(item);
            if (item == "dare") DressDareItem(mi);
            if (item == "report")
            {
                foreach (var reason in ReportReason.All)
                {
                    var sub = MenuItem("report_" + reason);
                    var rr = reason;
                    sub.Click += async (_, _) => await ReportAsync(f.Id, f.Id, rr);
                    mi.Items.Add(sub);
                }
            }
            else
            {
                var what = item;
                mi.Click += async (_, _) => await RunMenuAsync(f, what);
            }
            menu.Items.Add(mi);
        }
        return menu;
    }

    private static MenuItem MenuItem(string id)
    {
        bool warn = id is "block" or "report";
        return new MenuItem
        {
            Header = Loc.Get(id == "dare" ? "dares_menu_dare" : "friends_menu_" + id),
            Foreground = warn ? FriendsLook.RedBrush : FriendsLook.TextBrush,
            Background = Brushes.Transparent,
            Tag = "friends-menu-item:" + id,
        };
    }

    /// <summary>Runs one menu line. Remove and Block ask first when the confirm step is built
    /// (<see cref="AskFirst"/>); otherwise they run here. Internal for the suite.</summary>
    internal async Task RunMenuAsync(Friend f, string what)
    {
        if (what == "dare") { OpenDareSlip(f); return; }
        if (_svc == null) return;
        if (what is "remove" or "block")
        {
            bool asked = false;
            AskFirst(f, what, ref asked);
            if (!asked) await RemoveOrBlockAsync(f.Id, f.Name, what);
            return;
        }
        ActResult r = ActResult.TryLater;
        try
        {
            switch (what)
            {
                case "squelch": r = await _svc.SetSquelchAsync(f.Id, true); break;
                case "unsquelch": r = await _svc.SetSquelchAsync(f.Id, false); break;
                default: return;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] menu {What} failed: {E}", what, ex.Message); }
        if (r == ActResult.Done)
        {
            FriendsSfx.Click();
            ShowTimed(f.Id, Loc.Get(what == "squelch" ? "friends_squelch_done" : "friends_unsquelch_done"), true,
                TimeSpan.FromSeconds(FriendsDrawerRules.ResultHoldSeconds));
        }
        else ShowActResult(f.Id, r);
        await SafeRefreshAsync();
        Render();
    }

    /// <summary>A worded note in the row that is not a send result (a report went through).</summary>
    private void ShowNote(string friendId, string key, bool good = true, bool timed = false)
    {
        if (timed)
        {
            ShowTimed(friendId, Loc.Get(key), good, TimeSpan.FromSeconds(FriendsDrawerRules.ResultHoldSeconds));
            TellOutside(Loc.Get(key), good);   // the Goon room opening can take the screen and fold the drawer
            return;
        }
        if (_resultTimers.TryGetValue(friendId, out var old)) { old.Stop(); _resultTimers.Remove(friendId); }
        _results[friendId] = (Loc.Get(key), good);
        Render();
    }
}

/// <summary>
/// Where the Goon invite tile gets its live join code: the Goon Game page reports the room it is
/// hosting (<c>room-code</c>) and <see cref="GoonHostService.RoomCode"/> holds it. With no room yet
/// the tile opens one (<see cref="OpenGoonRoom"/>) and sends its code, so it is only disabled for
/// an account that cannot host. Backroom and Ramp carry no code.
/// </summary>
public static class InviteCodes
{
    /// <summary>The live Goon Game join code while this account is hosting, else null.</summary>
    public static Func<string?> GoonCode { get; set; } = () => GoonHostService.RoomCode;

    /// <summary>May this account host a Goon room (Prime)? Joining is free, hosting is not.</summary>
    public static Func<bool> CanHostGoon { get; set; } = () => GoonHostService.CanHost;

    /// <summary>Opens (or reuses) a Goon room and returns its code; Busy = a match is on.</summary>
    public static Func<TimeSpan, Task<(string? Code, bool Busy)>> OpenGoonRoom { get; set; }
        = GoonHostService.OpenRoomForInviteAsync;

    /// <summary>Opens the chess board on a challenge to a friend (by the friend's id) and returns
    /// the server's challenge id, else null.</summary>
    public static Func<string, TimeSpan, Task<string?>> ChallengeFriend { get; set; }
        = ConditioningControlPanel.Services.PieceByPiece.PieceByPieceHostService.ChallengeFriendAsync;

    /// <summary>The code to send with an invite, and the loc key of the reason the tile is
    /// disabled (null when it can be sent).</summary>
    internal static (string? Code, string? BlockedKey) For(string destination)
    {
        switch (destination)
        {
            case InviteDestination.Goon:
                var g = GoonCode();
                if (!string.IsNullOrEmpty(g)) return (g, null);
                // No room yet: the tile opens one, unless this account cannot host at all.
                return CanHostGoon() ? (null, null) : (null, "friends_invite_goon_prime");
            default:
                return (null, null);
        }
    }
}

/// <summary>
/// The Catalogue tab's list: library entries that came from the site catalogue, as
/// (catalogue id, title). The local library does not record a catalogue id yet, so this is
/// empty and the tab says so; whoever teaches the library that id fills <see cref="List"/>.
/// </summary>
public static class CatalogueWatches
{
    public static Func<IReadOnlyList<(string Id, string Title)>> List { get; set; }
        = () => Array.Empty<(string, string)>();
}
