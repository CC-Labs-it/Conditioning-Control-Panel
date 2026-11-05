// PORTED from ConditioningControlPanel/Controls/Friends/FriendsDrawer.Platforms.cs: where a friend is
// online from (Friends Everywhere S3). MDL2 PC1 / CellPhone / Globe become Fluent icons.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    /// <summary>Small marks after the name, desktop / phone / web, each with its tooltip ("On their phone").
    /// An offline friend or a server that sends no <c>platforms</c> draws none.</summary>
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
        foreach (var (platform, _, tipKey) in marks)
        {
            var tip = Loc.Get(tipKey);
            var mark = new Border
            {
                Background = Brushes.Transparent,   // hit-testable, so the tooltip shows (WPF :38)
                Margin = new Thickness(0, 1, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "friends-platform:" + platform,
                Child = new IconGlyph { Kind = PlatformIcon(platform), Size = 10.5, Foreground = Dim },
            };
            ToolTip.SetTip(mark, tip);
            AutomationProperties.SetName(mark, tip);
            strip.Children.Add(mark);
        }
        nameLine.Children.Add(strip);
    }

    /// <summary>WPF FriendsDrawerRules.PlatformGlyph (MDL2 PC1, CellPhone, Globe).</summary>
    internal static IconKind PlatformIcon(string platform) => platform switch
    {
        FriendPlatform.Phone => IconKind.Phone,
        FriendPlatform.Web => IconKind.Globe,
        _ => IconKind.Desktop,
    };
}
