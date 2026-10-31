using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    private static void SetupSync7()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task Sync7LinksCopyRenewClaimAndFallback() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupSync7();
        var (svc, wire) = Service();
        using (svc)
        {
            await svc.RefreshAsync();
            var now = DateTimeOffset.UtcNow;
            wire.LinkBody = "{\"ok\":true,\"token\":\"7K2Q9ABCDEFG\",\"expires_at\":\"" + now.AddHours(5).ToString("O") + "\",\"uses_left\":3}";
            var d = new FriendsDrawer(svc) { LinkClock = () => now };
            string? copied = null;
            var sounds = new System.Collections.Generic.List<string>(); d.LinkSound = sounds.Add;
            d.CopyText = text => { copied = text; return Task.FromResult(true); };
            var pressed = Tagged<Button>(d, "friends-copy-link");
            Assert.NotNull(pressed);
            Assert.True(await d.CopyLinkAsync());
            Assert.NotSame(pressed, Tagged<Button>(d, "friends-copy-link"));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.IsType<global::Avalonia.Media.ScaleTransform>(Tagged<Button>(d, "friends-copy-link")!.RenderTransform);
            var pop = Tagged<FriendsDrawer.LinkBurst>(d, "friends-link-pop")!;
            Assert.Equal(12, pop.Sparks); pop.Step(110);
            Assert.Equal(1.22, ((global::Avalonia.Media.ScaleTransform)Tagged<Button>(d, "friends-copy-link")!.RenderTransform!).ScaleX, 5);
            pop.Step(380); Assert.Equal(1, ((global::Avalonia.Media.ScaleTransform)Tagged<Button>(d, "friends-copy-link")!.RenderTransform!).ScaleX);
            Assert.Equal(TimeSpan.FromMilliseconds(ConditioningControlPanel.Services.Safety.GentleVisuals.RiseMs(180)),
                ((global::Avalonia.Animation.DoubleTransition)Tagged<TextBlock>(d, "friends-link-note")!.Transitions![0]).Duration);
            Assert.Equal("click", sounds.Last());
            Assert.NotEmpty(Tagged<TextBlock>(d, "friends-link-note")!.Transitions!);
            Assert.Equal("https://app.cclabs.app/f/7K2Q9ABCDEFG", copied);
            Assert.Equal(Loc.GetF("friends_link_copied_left_hours", 5, 3), d.LinkNoteText);
            Assert.True(await d.CopyLinkAsync());
            Assert.Equal(2, wire.Ops.Count(op => op == "link"));
            Assert.True(await d.NewLinkAsync()); Assert.Equal("click", sounds.Last());
            Assert.Contains("link_revoke", wire.Ops);
            var before = wire.Ops.Count(op => op == "state");
            Assert.Equal(AddResult.Accepted, await d.AddByCodeAsync("https://app.cclabs.app/f/ABCDEFGHJKLM"));
            Assert.Equal(before + 1, wire.Ops.Count(op => op == "state"));
            Assert.Equal("accepted", sounds.Last()); Assert.Equal(14, Tagged<FriendsDrawer.LinkBurst>(d, "friends-link-wave")!.Sparks);
            var box = Tagged<TextBox>(d, "friends-code-box")!;
            box.Text = "https://evil.example/f/7K2Q9ABCDEFG";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("", box.Text);
            box.Text = "https://app.cclabs.app/f/ABCDEFGHJKLM";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("ABCDEFGHJKLM", box.Text);
            Assert.True(Tagged<Button>(d, "friends-add-go")!.IsEnabled);
            box.Text = "";
            foreach (var c in "CCPABCDEFGHJ")
            {
                box.Text += c;
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal("CCPABCDEFGHJ", box.Text);
            Assert.False(Tagged<TextBlock>(d, "friends-code-prefix")!.IsVisible);
            wire.HoldLinks = new TaskCompletionSource();
            var slow = d.CopyLinkAsync();
            Assert.False(await d.NewLinkAsync());
            Assert.False(await d.CopyLinkAsync());
            wire.HoldLinks.SetResult();
            await slow;
        }
        var (offlineSvc, offlineWire) = Service();
        using (offlineSvc)
        {
            await offlineSvc.RefreshAsync();
            var sounds = new System.Collections.Generic.List<string>();
            var d = new FriendsDrawer(offlineSvc) { CopyText = _ => Task.FromResult(true), LinkSound = sounds.Add };
            Assert.False(await d.CopyLinkAsync());
            Assert.Equal(Loc.Get("friends_link_offline"), d.LinkNoteText); Assert.Equal("click", sounds.Last());
            d.CopyText = _ => Task.FromResult(false);
            Assert.False(await d.CopyLinkAsync());
            Assert.Equal(Loc.Get("friends_add_try_later"), d.LinkNoteText); Assert.Equal("denied", sounds.Last());
            Assert.False(await d.NewLinkAsync()); Assert.Equal("denied", sounds.Last());
            offlineWire.ClaimBody = """{"ok":true,"status":"unusable"}""";
            Assert.Equal(AddResult.NotFound, await d.AddByCodeAsync("ABCDEFGHJKLM")); Assert.Equal("denied", sounds.Last());
        }
    });

    [Fact]
    public Task Sync7PlatformMarksUseSharedOrderingAndTips() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupSync7();
        var (svc, wire) = Service();
        using (svc)
        {
            wire.StateBody = wire.StateBody.Replace("\"name\":\"Mia\"", "\"name\":\"Mia\",\"platforms\":[\"web\",\"phone\",\"desktop\"]");
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            foreach (var platform in new[] { "desktop", "phone", "web" })
            {
                var mark = Tagged<Control>(d, "friends-platform:" + platform);
                Assert.NotNull(mark);
                Assert.Equal(Loc.Get("friends_platform_" + platform), ToolTip.GetTip(mark!));
            }
        }
    });
    [Fact]
    public Task RaceForwardsRoomsAndRefusalsDecline() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var race = new InboxItem("abcdef0123456789", SendKind.Invite, "u_on", "Mia", null, null,
            InviteDestination.Race, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)) { Room = "abcdefghijkl" };
        var open = FriendsLanding.OpenRace; var service = FriendsLanding.Service; var lab = CoreEntitlement.HasLabProvider;
        var (svc, wire) = Service(); string? room = null;
        try
        {
            await svc.RefreshAsync();
            CoreEntitlement.HasLabProvider = () => false; Assert.Null(GameWindow.LaunchFriendRace(race.Room));
            Assert.Equal("join=abcdefghijkl", GameWindow.RaceQuery(GameWindow.Games[GameWindow.GlassroadId], race.Room));
            FriendsLanding.Service = () => svc;
            FriendsLanding.OpenRace = r => { room = r; return true; };
            Assert.True(FriendsLanding.CanJoin(race)); FriendsLanding.OnKnockDone(race, KnockOutcome.Go);
            Assert.Equal(race.Room, room); await svc.TickAsync(); Assert.Contains("joined", wire.Polls.Last());
            FriendsLanding.OpenRace = _ => false; FriendsLanding.OnKnockDone(race with { Id = "0123456789abcdef" }, KnockOutcome.Go);
            await svc.TickAsync(); Assert.Contains("declined", wire.Polls.Last()); Assert.DoesNotContain("joined", wire.Polls.Last());
            Assert.False(FriendsLanding.CanJoin(race with { Destination = InviteDestination.Breakout }));
        }
        finally { FriendsLanding.OpenRace = open; FriendsLanding.Service = service; CoreEntitlement.HasLabProvider = lab; svc.Dispose(); }
    });
    [Fact]
    public Task RaceStartupHoldsRoomsUntilReady() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetupSync7(); var w = new GameWindow(GameWindow.Games[GameWindow.GlassroadId]);
        var frames = new System.Collections.Generic.List<string>(); w.Posted += frames.Add;
        w.HandoffRaceRoom("abcdefghijkl"); Assert.Empty(frames);
        w.HandleMessage("""{"type":"ready"}"""); Assert.Single(frames.Where(f => f.Contains("join-room") && f.Contains("abcdefghijkl")));
        w.HandleMessage("""{"type":"ready"}"""); Assert.Single(frames.Where(f => f.Contains("join-room")));
        w.Close(); w.HandoffRaceRoom("mnopqrstuvwx"); Assert.DoesNotContain(frames, f => f.Contains("mnopqrstuvwx"));
        var closed = new GameWindow(GameWindow.Games[GameWindow.GlassroadId]); closed.Posted += frames.Add;
        closed.HandoffRaceRoom("abcdefgh1234"); closed.Close(); closed.HandleMessage("""{"type":"ready"}"""); Assert.DoesNotContain(frames, f => f.Contains("abcdefgh1234"));
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public Task LinkMotionUsesGentleAndOsCaps(bool gentle, bool os) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetupSync7(); var probe = UiMotionPreference.OsProbe;
        try
        {
            CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full; CoreSettings.Current.GentleVisuals = gentle;
            UiMotionPreference.OsProbe = () => os; UiMotionPreference.CacheStamp = long.MinValue;
            var d = new FriendsDrawer(); d.PlayLinkFx(new Border(), false);
            Assert.Equal(6, Tagged<FriendsDrawer.LinkBurst>(d, "friends-link-pop")!.Sparks);
        }
        finally { UiMotionPreference.OsProbe = probe; UiMotionPreference.CacheStamp = long.MinValue; }
        return Task.CompletedTask;
    });
}
