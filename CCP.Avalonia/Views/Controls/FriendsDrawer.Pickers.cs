// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Pickers.cs (+ FriendsDrawer.cs
// BuildCard/ActionButton/ShowResult): the card's Invite / Poke / Send a watch buttons and the picker
// each opens inline in the card, over IFriendsService.
// The action buttons carry WPF's MDL2 glyphs as Fluent icons (E8A7/E7C9/E714, fluent-icons 2026-10-10).
// WPF FriendsDrawer.cs:714,1144: action click and send-result cues.
// The Goon tile stays shut here: GoonHostService is WPF-only, so this head
// cannot open a Goon room. Chess challenges use the shared game host.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Linq;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private static readonly IBrush Ground = new SolidColorBrush(Color.FromRgb(0x10, 0x0A, 0x1E));
    // WPF 67fadbf70f DareLook.Gold: the watch stamps' edge.
    private static readonly Color StampGold = DareLook.ToneColor(DareTone.Sp);

    /// <summary>The picker open inside the open card: "poke", "invite", "watch" or null.</summary>
    private string? _picker;
    private string _watchTab = "flavour";
    internal string? OpenPicker => _picker;

    /// <summary>Opens a picker in a friend's card (the suite's way to press an action).</summary>
    internal void OpenPickerFor(string friendId, string picker) { (_openId, _confirm, _picker) = (friendId, null, picker); Render(); }

    /// <summary>WPF 67fadbf70f BuildCard: Invite, Poke, Send a watch and More as pressable tiles two by two
    /// (FriendsDrawerRules.CardActions), each a hued block and bold print; the open one sits pressed in.</summary>
    private Control CardActions(Friend f)
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var act in FriendsDrawerRules.CardActions)
        {
            // WPF FriendsDrawer.cs ActionMark: the Dares kind colours.
            var (kind, hue) = act switch
            {
                "invite" => (IconKind.Mail, DareLook.ToneColor(DareTone.Minutes)),
                "poke" => (IconKind.HandPoint, DareLook.ToneColor(DareTone.Video)),
                "watch" => (IconKind.Video, DareLook.ToneColor(DareTone.Bubbles)),
                _ => (IconKind.MoreHorizontal, DareLook.ToneColor(DareTone.CameraOff)),
            };
            var text = Loc.Get("friends_action_" + act);
            var b = DareLook.PressTile(DareLook.Labelled(DareLook.Block(kind, hue), text), hue, act != "more" && _picker == act, "friends-action:" + act, text);
            b.Margin = grid.Children.Count % 2 == 0 ? new Thickness(0, 0, 3, 6) : new Thickness(3, 0, 0, 6);
            if (act == "more") b.Click += (_, _) => { Platform.FriendsSfx.Click(); Menu(f).Open(b); };   // WPF FriendsDrawer.cs:686
            else b.Click += (_, _) => { Platform.FriendsSfx.Click(); _picker = _picker == act ? null : act; Render(); };
            grid.Children.Add(b);
        }
        return grid;
    }

    private Control Picker(Friend f) => _picker switch
    {
        "poke" => PokePicker(f),
        "invite" => InvitePicker(f),
        _ => WatchPicker(f),
    };

    private Control PokePicker(Friend f)
    {
        // WPF 67fadbf70f: word stamps on the poke-pink edge.
        var wrap = new WrapPanel { Tag = "friends-picker:poke", Margin = new Thickness(0, 0, -8, -8) };
        foreach (var id in PokeSet.Shipped)
        {
            Button chip = null!;
            chip = DareLook.Choice(Loc.Get("friends_poke_" + id), DareLook.ToneColor(DareTone.Video), null,
                () => { PlayLinkFx(chip, false, Pink); _ = PokeAsync(f.Id, id); }, "friends-poke:" + id, compact: true);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    internal async Task<SendResult> PokeAsync(string friendId, string pokeId)
    {
        if (_svc == null || !PokeSet.IsValid(pokeId)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.PokeAsync(friendId, pokeId); } catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    /// <summary>One tile per sendable destination, in WPF's order and art (Remote is gone, owner 2026-09-28).</summary>
    internal static readonly (string Id, string Art)[] InviteTiles =
    {
        (InviteDestination.Goon, "goon_game_tile.png"), (InviteDestination.BackRoom, "backroom.png"),
        (InviteDestination.Ramp, "Phrase_Lock.png"), (InviteDestination.Chess, "piecebypiece.png"),
    };

    private Control InvitePicker(Friend f)
    {
        var grid = new UniformGrid { Columns = 2, Tag = "friends-picker:invite", Margin = new Thickness(0, 0, 0, -6) };
        foreach (var (id, art) in InviteTiles)
        {
            var tile = InviteTile(id, art, FriendsInviteCodes.BlockedKey(id) != null);
            tile.Margin = grid.Children.Count % 2 == 0 ? new Thickness(0, 0, 3, 6) : new Thickness(3, 0, 0, 6);
            if (FriendsInviteCodes.BlockedKey(id) is { } blocked)
            {
                tile.IsEnabled = false;
                ToolTip.SetShowOnDisabled(tile, true);
                ToolTip.SetTip(tile, Loc.Get(blocked));
            }
            tile.Click += async (_, _) =>
            {
                PlayLinkFx(tile, false, Lilac);
                if (id == InviteDestination.Goon) await InviteAsync(f.Id, id, FriendsInviteCodes.GoonCode());
                else if (id == InviteDestination.Chess) await InviteToChessAsync(f.Id);
                else await InviteAsync(f.Id, id, null);
            };
            grid.Children.Add(tile);
        }
        return grid;
    }

    /// <summary>WPF 67fadbf70f InviteTile: the picture inset on a light tile, the name in print; a locked tile
    /// wears a padlock block and stays visible.</summary>
    private static Button InviteTile(string id, string art, bool locked)
    {
        var sp = new StackPanel();
        if (Art(art) is { } pic)
        {
            var holder = new Border { Height = 58, CornerRadius = new CornerRadius(4), ClipToBounds = true,
                Child = new Grid { Children = { new Image { Source = pic, Stretch = Stretch.UniformToFill, Height = 58 } } } };
            if (locked)
            {
                var badge = DareLook.Block(IconKind.LockClosed, DareLook.Ink, 24);
                (badge.HorizontalAlignment, badge.VerticalAlignment, badge.Margin, badge.Tag) =
                    (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 4, 4, 0), "friends-invite-lock");
                ((Grid)holder.Child).Children.Add(badge);
            }
            sp.Children.Add(holder);
        }
        var name = Loc.Get("friends_invite_" + id);
        sp.Children.Add(new TextBlock { Text = name, FontFamily = DareLook.PrintFont, FontSize = 15, FontWeight = FontWeight.Bold,
            Foreground = DareLook.CardInkBrush, Margin = new Thickness(3, 4, 3, 1) });
        return DareLook.PressTile(sp, DareLook.ToneColor(DareTone.Minutes), false, "friends-invite:" + id, name, new Thickness(4));
    }

    private static readonly Dictionary<string, Bitmap?> ArtCache = new();
    private static Bitmap? Art(string file)
    {
        if (ArtCache.TryGetValue(file, out var hit)) return hit;
        Bitmap? bmp = null;
        try
        {
            var uri = new Uri("avares://CCP.Avalonia/Resources/features/" + file);
            if (AssetLoader.Exists(uri)) { using var s = AssetLoader.Open(uri); bmp = Bitmap.DecodeToWidth(s, 256); }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] invite art {File}: {E}", file, ex.Message); }
        return ArtCache[file] = bmp;
    }

    private bool _openingChess;

    /// <summary>WPF InviteToChessAsync: open the board on a challenge, then send its id. One tap.</summary>
    internal async Task<SendResult?> InviteToChessAsync(string friendId)
    {
        if (_openingChess) return null;
        _openingChess = true;
        ShowNote(friendId, "friends_invite_chess_opening");
        string? challenge;
        try { challenge = await FriendsInviteCodes.ChallengeFriend(friendId, TimeSpan.FromSeconds(45)); }
        catch { challenge = null; }
        finally { _openingChess = false; }
        if (!InviteDestination.IsChallengeId(challenge))
        {
            var text = Loc.Get("friends_invite_chess_failed");
            ShowTimed(friendId, text, false);
            if (!_isOpen) Say(text, false);
            return null;
        }
        return await InviteAsync(friendId, InviteDestination.Chess, challenge);
    }

    internal async Task<SendResult> InviteAsync(string friendId, string destination, string? code)
    {
        if (_svc == null || !InviteDestination.IsSendable(destination)) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.InviteAsync(friendId, destination, code); } catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    private static readonly string[] WatchTabs = { "catalogue", "flavour", "ht" };

    private Control WatchPicker(Friend f)
    {
        var sp = new StackPanel { Tag = "friends-picker:watch" };
        // WPF 67fadbf70f: the three sources as a switch of word stamps on the watch blue; the one showing is pressed in.
        var tabs = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 4), Tag = "friends-watch-switch" };
        foreach (var tab in WatchTabs)
        {
            var b = DareLook.Choice(Loc.Get("friends_watch_tab_" + tab), DareLook.ToneColor(DareTone.Bubbles), null,
                () => { _watchTab = tab; Render(); }, "friends-watch-tab:" + tab, compact: true, on: _watchTab == tab);
            b.Margin = new Thickness(tabs.Children.Count == 0 ? 0 : 3, 0, tabs.Children.Count == WatchTabs.Length - 1 ? 0 : 3, 6);
            tabs.Children.Add(b);
        }
        // WPF e0065f6b76: one size for all three, so a long word shrinking alone never reads as another button.
        tabs.SizeChanged += (_, _) => EvenOutWords(tabs);
        sp.Children.Add(tabs);
        if (_watchTab == "catalogue") sp.Children.Add(CatalogueList(f));
        else if (_watchTab == "ht") sp.Children.Add(HtBox(f));
        else
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, -8, -8) };
            foreach (var fl in WatchRef.Flavours)
            {
                var title = Loc.Get("friends_flavour_" + fl);
                Button chip = null!;
                chip = DareLook.Choice(title, StampGold, null,
                    () => { PlayLinkFx(chip, false, Gold); _ = SendWatchAsync(f.Id, new WatchRef(WatchKind.Flavour, fl, title)); }, "friends-flavour:" + fl, compact: true);
                chip.HorizontalAlignment = HorizontalAlignment.Left;
                wrap.Children.Add(chip);
            }
            sp.Children.Add(wrap);
        }
        return sp;
    }

    private Control CatalogueList(Friend f)
    {
        var items = FriendsInviteCodes.CatalogueWatches();
        if (items.Count == 0) return Tagged(Wrap(Label(Loc.Get("friends_watch_catalogue_empty"), 12, Muted)), "friends-watch-catalogue-empty");
        var sp = new StackPanel();
        foreach (var (id, title) in items)
        {
            Button b = null!;
            b = DareLook.Choice(title, StampGold, null,
                () => { PlayLinkFx(b, false, Gold); _ = SendWatchAsync(f.Id, new WatchRef(WatchKind.Catalogue, id, title)); }, "friends-catalogue:" + id, compact: true);
            b.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(b);
        }
        return sp;
    }

    private Control HtBox(Friend f)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var box = new TextBox
        {
            FontFamily = Mono, FontSize = 13, Foreground = Text, Background = Ground, BorderBrush = Line2,
            CaretBrush = Lilac, Padding = new Thickness(6, 3, 6, 3), Tag = "friends-ht-box",
        };
        ToolTip.SetTip(box, Loc.Get("friends_watch_ht_hint"));
        Button send = null!;
        send = DareLook.Choice(Loc.Get("friends_watch_send"), StampGold, null, () =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id.Length == 0) return;
            PlayLinkFx(send, false, Gold);
            _ = SendWatchAsync(f.Id, new WatchRef(WatchKind.Ht, id, null));
        }, "friends-ht-send", compact: true);
        (send.Margin, send.IsEnabled) = (new Thickness(6, 0, 0, 0), false);
        // Text property, not TextChanged: Avalonia raises that one later, so the Send state lagged a frame.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            var n = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (n != box.Text) { box.Text = n; box.CaretIndex = n.Length; }
            send.IsEnabled = n.Length > 0;
        };
        Grid.SetColumn(send, 1);
        g.Children.Add(box);
        g.Children.Add(send);
        return g;
    }

    internal async Task<SendResult> SendWatchAsync(string friendId, WatchRef watch)
    {
        if (_svc == null || !watch.IsValid()) return SendResult.Refused;
        SendResult r;
        try { r = await _svc.SendWatchAsync(friendId, watch); } catch { r = SendResult.TryLater; }
        ShowResult(friendId, r);
        return r;
    }

    /// <summary>WPF e0065f6b76 EvenOutWords: the switch's words share the size at which the longest fits its cell,
    /// with slimmer sides (a compact choice shrinks each word on its own, which made one cell read smaller).</summary>
    private static void EvenOutWords(Panel cells)
    {
        var words = new List<(TextBlock Text, Border Face)>();
        foreach (var b in cells.Children.OfType<Button>())
            if (b.GetLogicalDescendants().OfType<Viewbox>().FirstOrDefault() is { Child: TextBlock t, Parent: Border face }) words.Add((t, face));
        double scale = 1;
        foreach (var (text, face) in words)
        {
            face.Padding = new Thickness(4, face.Padding.Top, 4, face.Padding.Bottom);
            // One px under the box: layout rounding can take a pixel off the Viewbox.
            var room = face.Bounds.Width - 8 - face.BorderThickness.Left - face.BorderThickness.Right - 1;
            if (room <= 0) return;   // not laid out yet
            text.FontSize = DareLook.CompactWord;
            text.Measure(Size.Infinity);
            if (text.DesiredSize.Width > room) scale = Math.Min(scale, room / text.DesiredSize.Width);
        }
        var size = Math.Floor(DareLook.CompactWord * scale * 2) / 2;   // half-point steps, never above the stock size
        foreach (var (text, _) in words) text.FontSize = size;
    }

    /// <summary>WPF ShowResult: worded in the row; a drawer that folded while the send was out says it outside.</summary>
    private void ShowResult(string friendId, SendResult r)
    {
        // WPF FriendsDrawer.cs:1144: cheer only after a successful send.
        if (FriendsDrawerRules.IsGood(r)) Platform.FriendsSfx.Sent(); else Platform.FriendsSfx.Denied();
        var text = Loc.Get(FriendsDrawerRules.SendResultKey(r));
        ShowTimed(friendId, text, FriendsDrawerRules.IsGood(r));
        if (!_isOpen) Say(text, FriendsDrawerRules.IsGood(r));
    }

    /// <summary>WPF ShowNote (untimed): a line that stays until the next result replaces it.</summary>
    private void ShowNote(string friendId, string key)
    {
        if (_resultTimers.Remove(friendId, out var previous)) previous.Dispose();
        _results[friendId] = (Loc.Get(key), true);
        Render();
    }
}

/// <summary>WPF InviteCodes + CatalogueWatches. Chess uses the game host; Goon waits for its host.</summary>
internal static class FriendsInviteCodes
{
    public static Func<string?> GoonCode { get; set; } = () => null;
    public static Func<string, TimeSpan, Task<string?>> ChallengeFriend { get; set; } = ConditioningControlPanel.Avalonia.Views.Games.GameWindow.ChallengePbpFriend;
    public static Func<bool> HostsChess { get; set; } = () => true;
    public static Func<IReadOnlyList<(string Id, string Title)>> CatalogueWatches { get; set; } = () => Array.Empty<(string, string)>();

    /// <summary>The loc key of why a tile is shut, or null when it can be sent.</summary>
    internal static string? BlockedKey(string destination) => destination switch
    {
        InviteDestination.Goon when string.IsNullOrEmpty(GoonCode()) => "exclusives_not_on_this_build",
        InviteDestination.Chess when !HostsChess() => "exclusives_not_on_this_build",
        _ => null,
    };
}
