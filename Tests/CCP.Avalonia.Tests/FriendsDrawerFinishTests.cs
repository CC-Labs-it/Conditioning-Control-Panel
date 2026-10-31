using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Automation;
using ConditioningControlPanel.Services.GoonGame;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsDrawerFinishTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static T? Find<T>(Control d, string tag) where T : Control =>
        d.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);
    private static void Click(Control d, string tag) =>
        Assert.IsType<Button>(Find<Button>(d, tag)).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    // WPF FriendsDrawer.Blocked.cs:66-73: asking Remove/Block clears the picker, even after Keep.
    [Theory]
    [InlineData("remove")]
    [InlineData("block")]
    public Task ConfirmationForgetsTheSendPicker(string what) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        using (svc)
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            d.Toggle("u_on");
            Click(d, "friends-action:poke");
            Assert.Equal("poke", d.OpenPicker);
            // Use the real row menu handler, without opening a native popup.
            var row = Find<Border>(d, "friends-row:u_on")!;
            var menu = row.ContextMenu!;
            menu.ItemsSource!.OfType<MenuItem>().Single(i => i.Tag as string == "friends-menu-item:" + what)
                .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.NotNull(Find<Border>(d, "friends-confirm:" + what));
            Assert.Null(d.OpenPicker);
            Assert.Null(Find<WrapPanel>(d, "friends-picker:poke"));
            Click(d, "friends-confirm-keep");
            Assert.Null(Find<Border>(d, "friends-confirm:" + what));
            Assert.Null(Find<WrapPanel>(d, "friends-picker:poke"));
            Assert.DoesNotContain(what, wire.Ops);
            Assert.Empty(wire.Sends);
        }
    });

    // WPF FriendsDrawer.cs:668-700,714: actions stay visible; a question suppresses their picker.
    [Fact]
    public Task ConfirmationKeepsActionsAboveTheQuestion() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        using (svc)
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            await d.RunMenuAsync(svc.Snapshot.Friends.Single(f => f.Id == "u_on"), "block");
            var card = Find<StackPanel>(d, "friends-card")!;
            Assert.IsType<global::Avalonia.Controls.Primitives.UniformGrid>(card.Children[0]);
            Assert.Same(Find<Button>(d, "friends-action:more"), card.Children[1]);
            Assert.Same(Find<Border>(d, "friends-confirm:block"), card.Children[2]);
            Click(d, "friends-action:poke");
            Assert.NotNull(Find<Border>(d, "friends-confirm:block"));
            Assert.Null(Find<WrapPanel>(d, "friends-picker:poke"));
            Click(d, "friends-confirm-keep");
            Assert.NotNull(Find<WrapPanel>(d, "friends-picker:poke"));
            Assert.DoesNotContain("block", wire.Ops);
            Assert.Empty(wire.Sends);
        }
    });

    [Fact]
    public Task LockDayAndTierPlatesAppearOnTheRealRows() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        wire.StateBody = wire.StateBody.Replace("\"tier\":0", "\"tier\":2");
        var oldArt = FriendsDrawer.TierArt;
        using var art = new WriteableBitmap(new PixelSize(8, 4), new Vector(96, 96));
        FriendsDrawer.TierArt = _ => art;
        try
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc) { MeTier = () => 1 };
            d.Render();
            Assert.Equal(15, Find<Image>(d, "friends-tier-1")!.Height);
            var row = Find<Border>(d, "friends-row:u_on")!;
            Assert.Equal(12, Find<Image>(row, "friends-tier-2")!.Height);
            var chip = Find<Border>(row, "friends-lock")!;
            Assert.Contains(chip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.GetF("friends_lock_day", 3));
            Assert.Null(Find<Border>(Find<Border>(d, "friends-row:u_off")!, "friends-lock"));
            Assert.Null(FriendsDrawer.TierPlate(0, 12));
            // WPF FriendsDrawer.cs:566-587: the lock chip and hosting Join occupy separate columns.
            d.CanJoinTables = () => true;
            d.Tables = () => GoonOpenTablesApi.Parse("""{"ok":true,"tables":[{"code":"MIA123","name":"Mia","friend":true,"friendId":"u_on","cardSec":60}]}""");
            d.Render();
            row = Find<Border>(d, "friends-row:u_on")!;
            chip = Find<Border>(row, "friends-lock")!;
            Assert.Equal(2, Grid.GetColumn(chip));
            Assert.Equal(3, Grid.GetColumn(Find<Button>(row, "friends-table-join")!));
            FriendsDrawer.TierArt = _ => null;
            d.Render();
            Assert.Null(Find<Image>(d, "friends-tier-1"));
            Assert.Null(Find<Image>(d, "friends-tier-2"));
        }
        finally { FriendsDrawer.TierArt = oldArt; svc.Dispose(); }
    });

    [Fact]
    public Task NoticesBellTogglesTheLandingSettingAndTooltip() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        bool old = CoreSettings.Current.FriendNotificationsEnabled;
        try
        {
            CoreSettings.Current.FriendNotificationsEnabled = true;
            using var svc = FriendsDrawerTests.Service().Svc;
            var d = new FriendsDrawer(svc);
            Assert.Equal(Loc.Get("friends_notices_on"), AutomationProperties.GetName(Find<Button>(d, "friends-notices")!));
            Click(d, "friends-notices");
            Assert.False(CoreSettings.Current.FriendNotificationsEnabled);
            Assert.Equal(Loc.Get("friends_notices_off"), ToolTip.GetTip(Find<Button>(d, "friends-notices")!));
            Click(d, "friends-notices");
            Assert.True(CoreSettings.Current.FriendNotificationsEnabled);
            Assert.Equal(Loc.Get("friends_notices_on"), ToolTip.GetTip(Find<Button>(d, "friends-notices")!));
        }
        finally { CoreSettings.Current.FriendNotificationsEnabled = old; CoreSettings.Save(); }
        return Task.CompletedTask;
    });

    // WPF FriendsDrawer.Honesty.cs:80-116: all reasons use the requester id and word the result in-row.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task IncomingRequestReportsWordTheResultInTheRequestRow(bool succeeds) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        using (svc)
        {
            wire.ActSucceeds = succeeds;
            wire.StateBody = wire.StateBody.Replace("\"outgoing\":[]", "\"outgoing\":[{\"id\":\"u_out\",\"name\":\"Cat\",\"at\":\"2026-09-01T00:00:00Z\"}]");
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            Assert.Null(Find<Border>(d, "friends-request-out:u_out")!.ContextMenu);
            var menu = Find<Border>(d, "friends-request-in:u_req")!.ContextMenu!;
            var items = menu.ItemsSource!.Cast<MenuItem>().ToArray();
            Assert.Equal(new[] { "friends-menu-item:block", "friends-menu-item:report" }, items.Select(i => i.Tag));
            var reasons = items[1].ItemsSource!.Cast<MenuItem>().ToArray();
            Assert.Equal(ReportReason.All.Select(r => "friends-menu-item:report_" + r), reasons.Select(r => r.Tag));
            var expected = Loc.Get(succeeds ? "friends_report_done" : FriendsDrawerRules.ActResultKey(ActResult.TryLater));
            var reports = 0;
            foreach (var reason in reasons)
            {
                reason.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                reports++;
                await FriendsDrawerTests.Until(() => wire.Reports.Count == reports && Find<TextBlock>(Find<Border>(d, "friends-request-in:u_req")!, "friends-result")?.Text == expected);
                Assert.Equal(expected, Find<TextBlock>(Find<Border>(d, "friends-request-in:u_req")!, "friends-result")?.Text);
            }
            Assert.Equal(ReportReason.All.Count, wire.Reports.Count);
            foreach (var reason in ReportReason.All)
                Assert.Contains(wire.Reports, body => body.Contains("u_req") && body.Contains(reason));
        }
    });
}
