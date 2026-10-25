// Adapted from WPF FriendsDrawer.cs:322-377, 550-580, 872-897 and .Blocked.cs.
// Trunk f8a56b299 contributes the tier art recipe; existing head code remains authoritative.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private readonly Border _ask = new() { Margin = new Thickness(10, 8, 10, 0), Tag = "friends-presence-ask" };
    internal Func<int> MeTier { get; set; } = () => (int)(AccountSeed.Patreon?.CurrentTier ?? Models.PatreonTier.None);
    internal static Func<int, IImage?> TierArt { get; set; } = TierBadge.TierArt;
    internal static Control? TierPlate(int tier, double height)
    {
        if (tier <= 0 || TierArt(tier >= 2 ? 2 : 1) is not { } art) return null;
        return new Image { Source = art, Height = height, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Tag = tier >= 2 ? "friends-tier-2" : "friends-tier-1" };
    }

    private void RenderAsk()
    {
        _ask.Child = null;
        _ask.IsVisible = _svc?.Available == true && !Shared() && !PresenceAsk.Asked();
        if (!_ask.IsVisible) return;
        var words = new StackPanel { Children = {
            Wrap(Label(Loc.Get("friends_presence_ask"), 12.5, Text, weight: FontWeight.SemiBold)),
            Wrap(Label(Loc.Get("friends_presence_ask_sub"), 11, Muted)) } };
        var yes = Pill(Loc.Get("friends_presence_yes"), Mint, MintInk, "friends-presence-yes", Mint);
        yes.Click += (_, _) => {
            try { if (_svc != null) _svc.PresenceShared = true; } catch { }
            PresenceAsk.MarkAsked();
            Render();
        };
        var no = Pill(Loc.Get("friends_presence_no"), Raised, Text, "friends-presence-no", Line2);
        no.Margin = new Thickness(6, 0, 0, 0);
        no.Click += (_, _) => { PresenceAsk.MarkAsked(); Render(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0),
            Children = { yes, no } };
        Grid.SetColumn(buttons, 1);
        _ask.Child = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 6, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x5F, 0xFF, 0xD0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x5F, 0xFF, 0xD0)), BorderThickness = new Thickness(1),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { words, buttons } } };
    }

    private static Control? LockChip(Friend f)
    {
        if (f.Presence.LockDay is not int day) return null;
        return new Border { Tag = "friends-lock", CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x5F, 0xB4)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x5F, 0xB4)), BorderThickness = new Thickness(1),
            // WPF FriendsDrawer.cs:577: 10pt Mono in pink.
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = {
                new IconGlyph { Kind = IconKind.LockClosed, Size = 10, Foreground = Pink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) },
                Label(Loc.GetF("friends_lock_day", day), 10, Pink, Mono) } } };
    }

    private Button NoticesButton()
    {
        var glyph = new IconGlyph { Kind = IconKind.Alert, Size = 13 };
        var bell = Pill(glyph, Brushes.Transparent, Muted, "friends-notices");
        void Refresh()
        {
            bool on = CoreSettings.Current.FriendNotificationsEnabled;
            glyph.Foreground = on ? Lilac : Dim;
            var words = Loc.Get(on ? "friends_notices_on" : "friends_notices_off");
            ToolTip.SetTip(bell, words);
            AutomationProperties.SetName(bell, words);
        }
        bell.Click += (_, _) => {
            CoreSettings.Current.FriendNotificationsEnabled = !CoreSettings.Current.FriendNotificationsEnabled;
            try { CoreSettings.Save(); } catch { }
            Refresh();
        };
        Refresh();
        return bell;
    }

    private void RememberBlock(string id, string name)
    {
        if (_svc?.Snapshot.Blocked != null) return;
        FriendsBlockList.Shared.Add(FriendsBlockList.Account(), id, name, DateTimeOffset.UtcNow);
    }

    internal IReadOnlyList<BlockedEntry> BlockedRows()
    {
        if (_svc?.Snapshot.Blocked is not { } server) return FriendsBlockList.Shared.For(FriendsBlockList.Account());
        var rows = new List<BlockedEntry>();
        foreach (var b in server) rows.Add(new BlockedEntry(FriendsBlockList.Account() ?? "", b.Id, b.Name, DateTimeOffset.MinValue));
        return rows;
    }

    private ContextMenu RequestMenu(FriendRequest r)
    {
        var block = MenuItem("block");
        block.Click += async (_, _) => await BlockRequesterAsync(r);
        var report = MenuItem("report");
        var reasons = new List<MenuItem>();
        foreach (var reason in ReportReason.All)
        {
            var item = MenuItem("report_" + reason);
            item.Click += async (_, _) => {
                if (_svc == null) return;
                ActResult result;
                try { result = await _svc.ReportAsync(r.Id, reason); } catch { result = ActResult.TryLater; }
                ShowTimed("in:" + r.Id, Loc.Get(result == ActResult.Done ? "friends_report_done" : FriendsDrawerRules.ActResultKey(result)), result == ActResult.Done);
            };
            reasons.Add(item);
        }
        report.ItemsSource = reasons;
        return new ContextMenu { Tag = "friends-request-menu:" + r.Id, ItemsSource = new[] { block, report } };
    }

    internal async Task<ActResult> BlockRequesterAsync(FriendRequest r)
    {
        if (_svc == null) return ActResult.TryLater;
        ActResult result;
        try { result = await _svc.BlockAsync(r.Id); } catch { result = ActResult.TryLater; }
        if (result == ActResult.Done) {
            RememberBlock(r.Id, r.Name);
            Say(Loc.GetF("friends_blocked_done", r.Name), true);
        }
        else ShowTimed("in:" + r.Id, Loc.Get(FriendsDrawerRules.ActResultKey(result)), false);
        await SafeRefreshAsync();
        Render();
        return result;
    }
}
