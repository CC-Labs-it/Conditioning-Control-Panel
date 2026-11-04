using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
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

    [Fact]
    public Task PresentationUpdatesAndBodyOpensSender() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldMotion = CoreSettings.Current.MotionLevel;
        var oldGentle = CoreSettings.Current.GentleVisuals;
        var fetch = KnockCard.FetchPicture;
        string? url = null, opened = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            using var image = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/Phrase_Lock.png"));
            using var bytes = new System.IO.MemoryStream();
            image.CopyTo(bytes);
            KnockCard.FetchPicture = u => { url = u; return Task.FromResult(bytes.ToArray()); };
            var item = new InboxItem("notice1", SendKind.Poke, "u_on", "Mia", "/avatar", "unknown", null, null, null, DateTimeOffset.UtcNow.AddSeconds(-15), DateTimeOffset.UtcNow.AddHours(1));
            FriendNotices.ShowPoke(null, new FriendNotice(NoticeKind.Poke, "u_on", "Mia", item.At, 6000, item), _ => { }, _ => { }, p => opened = ((InboxItem)p!).FromId);
            var w = FriendNotices.Current!;
            var all = w.GetLogicalDescendants().ToArray();
            Assert.Contains(all, c => c is Grid { Tag: "notice-avatar" });
            Assert.EndsWith("/avatar", url);
            await FriendsDrawerTests.Until(() => all.OfType<Ellipse>().Any(e => e.Fill is ImageBrush));
            Assert.Contains(all.OfType<Ellipse>(), e => e.Fill is ImageBrush);
            var ago = all.OfType<TextBlock>().Single(x => Equals(x.Tag, "notice-ago"));
            Assert.EndsWith("s", ago.Text);
            var bar = all.OfType<ProgressBar>().Single(x => Equals(x.Tag, "notice-bar"));
            w.Tick(3000);
            Assert.Equal(0.5, bar.Value, 2);
            w.UpdateLayout();
            w.MouseDown(new Point(70, 15), MouseButton.Left);
            w.MouseUp(new Point(70, 15), MouseButton.Left);
            Assert.Equal("u_on", opened);
            Assert.False(FriendNotices.AnyUp);
            CoreSettings.Current.GentleVisuals = false;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            FriendNotices.ShowPoke(null, Notice("motion", item), _ => { }, _ => { });
            var animated = FriendNotices.Current!;
            var card = animated.GetLogicalDescendants().OfType<Border>().Single(c => c.Tag is FriendNotice);
            Assert.Equal(0, card.Opacity);
            Assert.Equal(28, ((TranslateTransform)card.RenderTransform!).X);
            animated.Dismiss(animated.Notices[0], false);
            Assert.True(animated.IsVisible); // WPF's fade keeps the card for 140 ms.
        }
        finally { FriendNotices.CloseAll(); KnockCard.FetchPicture = fetch; CoreSettings.Current.MotionLevel = oldMotion; CoreSettings.Current.GentleVisuals = oldGentle; }
        await Task.CompletedTask;
    });

    [Fact]
    public Task IncomingAndRefusedPokePlayWpfCues() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, _) = FriendsDrawerTests.Service();
        var (world, anchor, active, service, sound) = (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service, FriendsLanding.Sound);
        var bell = CoreSettings.Current.FriendNotificationsEnabled;
        var oldMotion = CoreSettings.Current.MotionLevel;
        var open = FriendsLanding.OpenFriend;
        string? opened = null;
        var cues = new List<(string, float, string, bool)>();
        var game = true;
        try
        {
            FriendsLanding.Sound = (p, v, t, i) => cues.Add((p, v, t, i));
            FriendsLanding.OpenFriend = id => opened = id;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, game);
            FriendsLanding.Anchor = () => null;
            FriendsLanding.ActiveGameWindow = () => null;
            FriendsLanding.Service = () => svc;
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.Tick();
            var item = new InboxItem("notice2", SendKind.Poke, "u_on", "Mia", null, "unknown", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            FriendsLanding.Router!.OnDelivered(item);
            Assert.Contains(("bubbles/Pop3.mp3", 0.10f, "friends-poke-in", true), cues);
            var w = FriendNotices.Current!;
            w.UpdateLayout();
            var back = w.GetLogicalDescendants().OfType<Button>().First();
            var point = back.TranslatePoint(new Point(back.Bounds.Width / 2, back.Bounds.Height / 2), w)!.Value;
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            Assert.Null(opened);
            await FriendsDrawerTests.Until(() => cues.Exists(c => c.Item3 == "friends-denied"));
            Assert.Contains(("chaos/ui_click.mp3", 0.12f, "friends-click", false), cues);
            Assert.Contains(("chaos/ui_denied.mp3", 0.14f, "friends-denied", false), cues);
            cues.Clear();
            game = false;
            FriendsLanding.Router.OnDelivered(new InboxItem("notice4", SendKind.Poke, "u_on", "Mia", null, "hi", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
            Assert.Contains(("bubbles/Pop3.mp3", 0.16f, "friends-poke-in", true), cues);
            var notice = FriendNotices.Current!;
            notice.UpdateLayout();
            notice.MouseDown(new Point(70, 15), MouseButton.Left);
            notice.MouseUp(new Point(70, 15), MouseButton.Left);
            Assert.Equal("u_on", opened);
            cues.Clear();
            CoreSettings.Current.FriendNotificationsEnabled = false;
            FriendsLanding.Router.OnDelivered(new InboxItem("notice3", SendKind.Poke, "u_on", "Mia", null, "hi", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
            Assert.Empty(cues);
        }
        finally
        {
            FriendsLanding.Stop(); StartupLadder.Inbox.Remove("friends:notice3");
            (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service, FriendsLanding.Sound) = (world, anchor, active, service, sound);
            CoreSettings.Current.FriendNotificationsEnabled = bell;
            CoreSettings.Current.MotionLevel = oldMotion;
            FriendsLanding.OpenFriend = open;
        }
    });

    [Fact]
    public Task UnknownAndEmptyPokesShowFallbackWords() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        try
        {
            foreach (var poke in new[] { "unknown", "" })
            {
                var item = new InboxItem("unknown", SendKind.Poke, "u_on", "Mia", null, poke, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
                FriendNotices.ShowPoke(null, Notice("u_on", item), _ => { }, _ => { });
                var line = FriendNotices.Current!.GetLogicalDescendants().OfType<TextBlock>().Single(x => Equals(x.Tag, "notice-line"));
                Assert.EndsWith(" hi", line.Text);
                FriendNotices.CloseAll();
            }
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });

}
