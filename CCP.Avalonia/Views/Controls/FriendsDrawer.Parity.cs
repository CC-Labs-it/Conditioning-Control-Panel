// Adapted from WPF FriendsDrawer.cs:310,550,566-587,872-897 and FriendsLook.cs:260-275.
using System;
using ConditioningControlPanel.Avalonia.Platform;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    internal Func<int> MeTier { get; set; } = () => (int)(AccountSeed.Patreon?.CurrentTier ?? Models.PatreonTier.None);
    internal static Func<int, IImage?> TierArt { get; set; } = TierBadge.TierArt;
    internal static Control? TierPlate(int tier, double height)
    {
        if (tier <= 0 || TierArt(tier >= 2 ? 2 : 1) is not { } art) return null;
        return new Image { Source = art, Height = height, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Tag = tier >= 2 ? "friends-tier-2" : "friends-tier-1" };
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

}
