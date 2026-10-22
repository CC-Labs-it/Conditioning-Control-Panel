using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// Where a friend is online from (Friends Everywhere S3): small marks after the name, desktop /
/// phone / web in that order, each with its own tooltip ("On their phone"). An offline friend or a
/// server that sends no <c>platforms</c> draws none, so the row looks as it always did.
/// </summary>
public sealed partial class FriendsDrawer
{
    private static readonly FontFamily PlatformGlyphFamily = new(FriendsDrawerRules.GlyphFont);

    private static void AddPlatformMarks(StackPanel nameLine, Friend f)
    {
        var marks = FriendsDrawerRules.PlatformMarks(f);
        if (marks.Count == 0) return;
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "friends-platforms",
        };
        foreach (var (platform, glyph, tipKey) in marks)
        {
            strip.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = PlatformGlyphFamily,
                FontSize = 10.5,
                Foreground = FriendsLook.DimBrush,
                Margin = new Thickness(0, 1, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                ToolTip = Loc.Get(tipKey),
                Tag = "friends-platform:" + platform,
            });
        }
        nameLine.Children.Add(strip);
    }
}
