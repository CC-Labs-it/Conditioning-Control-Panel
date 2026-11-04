using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FriendCornerNoticeTests
{
    private static FriendNotice Notice(string id, object payload) => new(NoticeKind.Poke, id, id, DateTimeOffset.UtcNow, 6000, payload);

    [Fact]
    public Task PokeStackFoldsPausesExpiresAndCallsActions() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var missed = "";
        var acted = "";
        Action<object?> miss = p => missed += p;
        Action<object?> act = p => acted += p;
        try
        {
            FriendNotices.ShowPoke(null, Notice("a", "old"), act, miss);
            var w = FriendNotices.Current!;
            Assert.False(w.ShowActivated);
            Assert.False(w.Focusable);
            Assert.False(w.ShowInTaskbar);
            Assert.False(w.IsActive);
            var screen = Assert.IsAssignableFrom<global::Avalonia.Platform.Screen>(w.Screens.Primary);
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            var (x, y) = FriendNoticeRules.Place(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale, w.Bounds.Width, w.Bounds.Height);
            Assert.Equal(new PixelPoint((int)Math.Round(x * scale), (int)Math.Round(y * scale)), w.Position);
            FriendNotices.ShowPoke(null, Notice("a", "new"), act, miss);
            Assert.Single(w.Notices);
            Assert.Equal(2, w.Notices[0].Count);
            FriendNotices.ShowPoke(null, Notice("b", "b"), act, miss);
            FriendNotices.ShowPoke(null, Notice("c", "c"), act, miss);
            FriendNotices.ShowPoke(null, Notice("d", "d"), act, miss);
            Assert.Equal(1, w.Hidden);
            w.Pause(true);
            w.Tick(7000);
            Assert.Equal(4, w.Notices.Count);
            w.Pause(false);
            w.Dismiss(w.Notices[0], true);
            Assert.Equal("d", acted);
            w.Tick(7000);
            Assert.Contains("new", missed);
            Assert.Contains("b", missed);
            Assert.Contains("c", missed);
            Assert.False(FriendNotices.AnyUp);
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HoldFoldReportsUnseenAndDismissDoesNot() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var missed = "";
        try
        {
            FriendNotices.ShowPoke(null, Notice("a", "a"), _ => { }, p => missed += p);
            var w = FriendNotices.Current!;
            FriendNotices.ShowPoke(null, Notice("b", "b"), _ => { }, p => missed += p);
            w.Dismiss(w.Notices.First(), false);
            FriendNotices.CloseAll(fold: true);
            Assert.Equal("a", missed);
            Assert.False(FriendNotices.AnyUp);
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task DeliveryHoldAndInboxReopenUsePokeNotice() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, wire) = FriendsDrawerTests.Service();
        var (world, anchor, active, service) = (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service);
        var bell = CoreSettings.Current.FriendNotificationsEnabled;
        var holding = false;
        const string id = "00000000000000a1";
        try
        {
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, true, holding);
            FriendsLanding.Anchor = () => null;
            FriendsLanding.ActiveGameWindow = () => null;
            FriendsLanding.Service = () => svc;
            FriendsLanding.Tick();
            await svc.RefreshAsync();
            var item = new InboxItem(id, SendKind.Poke, "u_on", "Mia", null, "hi", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            FriendsLanding.Router!.OnDelivered(item);
            Assert.True(FriendNotices.AnyUp);
            holding = true;
            FriendsLanding.Tick();
            Assert.False(FriendNotices.AnyUp);
            var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "friends:" + id);
            holding = false;
            StartupLadder.Inbox.Open(row);
            var w = FriendNotices.Current!;
            Assert.Single(w.Notices);
            w.Dismiss(w.Notices[0], true);
            Assert.Single(wire.Sends);
        }
        finally
        {
            FriendsLanding.Stop();
            StartupLadder.Inbox.Remove("friends:" + id);
            (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service) = (world, anchor, active, service);
            CoreSettings.Current.FriendNotificationsEnabled = bell;
        }
    });

}
