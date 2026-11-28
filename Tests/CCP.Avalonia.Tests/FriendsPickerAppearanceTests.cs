using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsPickerAppearanceTests
{
    [Fact]
    public Task PokePillsAndBlockedInviteMatchWpfAndClose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = FriendsInviteCodes.GoonCode;
        var (service, _) = FriendsDrawerTests.Service();
        Window? host = null;
        FriendsDrawer? drawer = null;
        try
        {
            FriendsInviteCodes.GoonCode = () => null;
            await service.RefreshAsync();
            drawer = new FriendsDrawer(service);
            host = new Window { Content = drawer }; host.Show();
            drawer.OpenPickerFor("u_on", "poke");
            var stamp = drawer.GetLogicalDescendants().OfType<Button>().First(x => (x.Tag as string)?.StartsWith("friends-poke:") == true);
            // WPF FriendsDrawer.Pickers.cs:35-36: radius 999 pill on the normal Line2 border.
            Assert.Equal(new CornerRadius(999), stamp.CornerRadius);
            Assert.Same(FriendsDrawer.Line2, stamp.BorderBrush);
            drawer.OpenPickerFor("u_on", "invite");
            var tile = drawer.GetLogicalDescendants().OfType<Button>().Single(x => (string?)x.Tag == "friends-invite:goon");
            Assert.False(tile.IsEnabled);
            Assert.NotNull(ToolTip.GetTip(tile));
            // WPF FriendsDrawer.Pickers.cs:80-86: disabled with its tooltip, no badge over the art.
            Assert.DoesNotContain(tile.GetLogicalDescendants(), x => x is Control c && (c.Tag as string)?.StartsWith("friends-invite-lock") == true);
            drawer.OnClosed();
            Assert.Null(drawer.OpenPicker);
        }
        finally { drawer?.OnClosed(); host?.Close(); service.Dispose(); FriendsInviteCodes.GoonCode = old; }
    });
}
