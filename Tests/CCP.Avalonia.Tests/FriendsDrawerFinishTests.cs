using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsDrawerFinishTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static T? Find<T>(Control d, string tag) where T : Control =>
        d.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);
    private static void Click(Control d, string tag) =>
        Assert.IsType<Button>(Find<Button>(d, tag)).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task PresenceAnswerIsOnceOnlyAndOnlyYesShares(bool yes) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var oldAsked = PresenceAsk.Asked;
        var oldMark = PresenceAsk.MarkAsked;
        bool asked = false;
        PresenceAsk.Asked = () => asked;
        PresenceAsk.MarkAsked = () => asked = true;
        var (svc, _) = FriendsDrawerTests.Service();
        try
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            Assert.True(Find<Border>(d, "friends-presence-ask")!.IsVisible);
            Click(d, yes ? "friends-presence-yes" : "friends-presence-no");
            Assert.True(asked);
            Assert.Equal(yes, svc.PresenceShared);
            Assert.False(Find<Border>(d, "friends-presence-ask")!.IsVisible);
            var reopened = new FriendsDrawer(svc);
            Assert.False(Find<Border>(reopened, "friends-presence-ask")!.IsVisible);
            asked = false;
            svc.PresenceShared = true;
            reopened.Render();
            Assert.False(Find<Border>(reopened, "friends-presence-ask")!.IsVisible);
            var signedOut = new FriendsDrawer(FriendsDrawerTests.Service(null).Svc);
            Assert.False(Find<Border>(signedOut, "friends-presence-ask")!.IsVisible);
            Assert.Equal(Loc.Get("friends_me_signed_out"), Find<TextBlock>(signedOut, "friends-me-status")!.Text);
        }
        finally { PresenceAsk.Asked = oldAsked; PresenceAsk.MarkAsked = oldMark; svc.Dispose(); }
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
            var d = new FriendsDrawer(FriendsDrawerTests.Service().Svc);
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

    [Fact]
    public Task OnlyIncomingRequestsCarryTheBlockAndReportMenu() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        wire.StateBody = wire.StateBody.Replace("\"outgoing\":[]", "\"outgoing\":[{\"id\":\"u_out\",\"name\":\"Cat\",\"via\":null,\"at\":\"2026-09-01T00:00:00Z\"}]");
        try
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            Assert.NotNull(Find<Border>(d, "friends-request-in:u_req")!.ContextMenu);
            // WPF FriendsDrawer.cs:819 attaches the menu inside if (incoming) only.
            Assert.Null(Find<Border>(d, "friends-request-out:u_out")!.ContextMenu);
        }
        finally { svc.Dispose(); }
    });

    [Fact]
    public Task RequestMenuBlocksAndReportsAndOldServersRememberPerAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var oldList = FriendsBlockList.Shared;
        var oldAccount = FriendsBlockList.Account;
        IReadOnlyList<BlockedEntry> entries = Array.Empty<BlockedEntry>();
        FriendsBlockList.Shared = new FriendsBlockList(() => entries, v => entries = v);
        FriendsBlockList.Account = () => "account-a";
        var (svc, wire) = FriendsDrawerTests.Service();
        wire.StateBody = wire.StateBody.Replace(",\"blocked\":[{\"id\":\"u_bad\",\"name\":\"Bob\"}]", "");
        try
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            var row = Find<Border>(d, "friends-request-in:u_req")!;
            var menu = row.ContextMenu!;
            var items = menu.ItemsSource!.Cast<MenuItem>().ToArray();
            Assert.Equal(new[] { "friends-menu-item:block", "friends-menu-item:report" }, items.Select(i => i.Tag));
            var report = items[1].ItemsSource!.Cast<MenuItem>().First();
            report.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await FriendsDrawerTests.Until(() => wire.Ops.Contains("report"));
            Assert.Contains("report", wire.Ops);
            // Real block menu handler, no confirmation for strangers.
            items[0].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await FriendsDrawerTests.Until(() => entries.Count == 1);
            Assert.Contains("block", wire.Ops);
            Assert.Equal("u_req", Assert.Single(d.BlockedRows()).Id);
            d.ToggleBlocked();
            Assert.NotNull(Find<Border>(d, "friends-blocked:u_req"));
            FriendsBlockList.Account = () => "account-b";
            Assert.Empty(d.BlockedRows());
            FriendsBlockList.Account = () => "account-a";
            await d.UnblockAsync(new BlockedFriend("u_req", "Ann"));
            Assert.Empty(entries);
            // A server-provided list is authoritative and retires the fallback without re-blocking.
            FriendsBlockList.Shared.Add("account-a", "old", "Old", DateTimeOffset.UtcNow);
            wire.StateBody = wire.StateBody.Replace("\"outgoing\":[]", "\"outgoing\":[],\"blocked\":[]");
            await svc.RefreshAsync();
            d.Render();
            Assert.Empty(d.BlockedRows());
            Assert.Empty(entries);
        }
        finally { svc.Dispose(); FriendsBlockList.Shared = oldList; FriendsBlockList.Account = oldAccount; FriendsBlockList.ResetMoved(); }
    });
}
