using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsDrawerHoverTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Border Row(Control root, string tag) => root.GetLogicalDescendants().OfType<Border>()
        .Single(c => c.Tag as string == tag);

    private static void Pointer(Border row, bool enter) => row.RaiseEvent(new PointerEventArgs(
        enter ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent,
        row, null!, row, default, 0, PointerPointProperties.None, KeyModifiers.None));

    private static void AssertWash(Border row) =>
        Assert.Equal(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(row.Background).Color);

    // WPF FriendsDrawer.cs:514-526,588,608-609; FriendsDrawer.Juice.cs:93-102.
    [Fact]
    public Task FriendHoverRestoresTheClosedOrHostingBrushAndKeepsAnOpenCardRaised() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        using var svc = FriendsDrawerTests.Service().Svc;
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        var row = Row(drawer, "friends-row:u_on");
        var rest = row.Background;
        Pointer(row, true);
        AssertWash(row);
        Pointer(row, false);
        Assert.Same(rest, row.Background);

        drawer.CanJoinTables = () => true;
        drawer.Tables = () => GoonOpenTablesApi.Parse("""{"ok":true,"tables":[{"code":"MIA123","name":"Mia","friend":true,"friendId":"u_on","cardSec":60}]}""");
        drawer.Render();
        row = Row(drawer, "friends-row:u_on");
        rest = Assert.IsType<LinearGradientBrush>(row.Background);
        Pointer(row, true);
        AssertWash(row);
        Pointer(row, false);
        Assert.Same(rest, row.Background);

        drawer.Toggle("u_on");
        row = Row(drawer, "friends-row:u_on");
        rest = row.Background;
        Pointer(row, true);
        Assert.Same(rest, row.Background);
        Pointer(row, false);
        Assert.Same(rest, row.Background);
    });

    // A pointer reaches the request row through the real window hit test.
    [Fact]
    public Task PointerInTheRequestMarginShowsAndClearsTheWash() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        using var svc = FriendsDrawerTests.Service().Svc;
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        var window = new Window { Width = 420, Height = 650, Content = drawer };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var row = Row(drawer, "friends-request-in:u_req");
            var rest = row.Background;
            window.MouseMove(new Point(2, 2));
            window.MouseMove(row.TranslatePoint(new Point(3, 3), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            AssertWash(row);
            window.MouseMove(new Point(2, 2));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(rest, row.Background);
        }
        finally { window.Close(); }
    });

    // WPF FriendsDrawer.cs:745-753,857-858; FriendsDrawer.Juice.cs:93-102.
    [Fact]
    public Task RequestRowsHaveAHitTestableRestingBrushAndRestoreItAfterHover() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        using (svc)
        {
            wire.StateBody = wire.StateBody.Replace("\"outgoing\":[]", "\"outgoing\":[{\"id\":\"u_out\",\"name\":\"Cat\",\"at\":\"2026-09-01T00:00:00Z\"}]");
            await svc.RefreshAsync();
            var drawer = new FriendsDrawer(svc);
            foreach (var tag in new[] { "friends-request-in:u_req", "friends-request-out:u_out" })
            {
                var row = Row(drawer, tag);
                var rest = Assert.IsAssignableFrom<ISolidColorBrush>(row.Background);
                Assert.Equal(Colors.Transparent, rest.Color);
                Pointer(row, true);
                AssertWash(row);
                Pointer(row, false);
                Assert.Same(rest, row.Background);
            }
        }
    });
}
