using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Services/Friends/FriendsLanding.cs:230-258,475-482 on this head: a landed poke comes up as a
/// corner notice with Poke back; a hold folds it into the Inbox, and the Inbox row brings the notice back.
/// Adapted from #4276 onto the existing passive stack (Views/Overlays/FriendNotices.cs).</summary>
public sealed class FriendPokeNoticeTests
{
    private static void Platform()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task DeliveryHoldAndInboxReopenUsePokeNotice() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, wire) = FriendsDrawerTests.Service();
        var (world, anchor, active, service, says) = (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service, FriendsLanding.EmiSays);
        var bell = CoreSettings.Current.FriendNotificationsEnabled;
        var holding = false;
        const string id = "00000000000000a1";
        try
        {
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.World = () => new LandingWorld(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false, SessionRunning: holding);
            FriendsLanding.Anchor = () => null;
            FriendsLanding.ActiveGameWindow = () => null;
            FriendsLanding.EmiSays = (_, _) => { };
            FriendsLanding.Service = () => svc;
            FriendsLanding.Tick();
            await svc.RefreshAsync();
            var item = new InboxItem(id, SendKind.Poke, "u_on", "Mia", null, "hi", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            FriendsLanding.Router!.OnDelivered(item);
            Assert.NotNull(FriendNotices.Current);
            Assert.Single(FriendNotices.Current!.Stack.All);
            Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key == "friends:" + id);

            holding = true;
            FriendsLanding.Tick();
            Assert.Null(FriendNotices.Current);
            var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "friends:" + id);

            holding = false;
            StartupLadder.Inbox.Open(row);
            var w = FriendNotices.Current!;
            var n = Assert.Single(w.Stack.All);
            w.Dismiss(n, true);   // Poke back
            await FriendsDrawerTests.Until(() => wire.Sends.Count > 0);
            Assert.Single(wire.Sends);
        }
        finally
        {
            FriendsLanding.Stop();
            FriendNotices.CloseAll();
            StartupLadder.Inbox.Remove("friends:" + id);
            (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service, FriendsLanding.EmiSays) = (world, anchor, active, service, says);
            CoreSettings.Current.FriendNotificationsEnabled = bell;
        }
    });

    [Fact]
    public Task BellOffFilesTheRowAndShowsNothing() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, _) = FriendsDrawerTests.Service();
        var (world, anchor, active, service) = (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service);
        var bell = CoreSettings.Current.FriendNotificationsEnabled;
        const string id = "00000000000000a2";
        try
        {
            CoreSettings.Current.FriendNotificationsEnabled = false;
            FriendsLanding.World = () => new LandingWorld(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false, SessionRunning: false);
            FriendsLanding.Anchor = () => null;
            FriendsLanding.ActiveGameWindow = () => null;
            FriendsLanding.Service = () => svc;
            FriendsLanding.Tick();
            FriendsLanding.Router!.OnDelivered(new InboxItem(id, SendKind.Poke, "u_on", "Mia", null, "hi", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
            Assert.Null(FriendNotices.Current);
            Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "friends:" + id);
        }
        finally
        {
            FriendsLanding.Stop();
            FriendNotices.CloseAll();
            StartupLadder.Inbox.Remove("friends:" + id);
            (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.Service) = (world, anchor, active, service);
            CoreSettings.Current.FriendNotificationsEnabled = bell;
        }
        await Task.CompletedTask;
    });
}
