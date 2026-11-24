using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    [Fact]
    public Task OnlyPopupClaimsEscape() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var svc = Service().Svc;
        var popup = new FriendsDrawer(svc);
        var page = new FriendsDrawer(svc, asPage: true);
        var popupButton = popup.GetLogicalDescendants().OfType<Button>().First();
        var pageButton = page.GetLogicalDescendants().OfType<Button>().First();
        Assert.True(EscapeClaim.InASurface(popupButton));
        Assert.False(EscapeClaim.InASurface(pageButton));
        return Task.CompletedTask;
    });

    [Fact]
    public Task SectionCountSitsAtRightInRegularTenPoint() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var svc = Service().Svc;
        var drawer = new FriendsDrawer(svc, asPage: true);
        await svc.RefreshAsync();
        drawer.Render();
        var title = drawer.GetLogicalDescendants().OfType<TextBlock>()
            .First(t => t.Text == Loc.Get("friends_section_online").ToUpperInvariant());
        var grid = Assert.IsType<Grid>(title.Parent);
        var count = Assert.IsType<TextBlock>(grid.Children[1]);
        Assert.Equal(10, title.FontSize);
        Assert.Equal(FontWeight.Normal, title.FontWeight);
        Assert.Equal(10, count.FontSize);
        Assert.Equal(HorizontalAlignment.Right, count.HorizontalAlignment);
    });
}
