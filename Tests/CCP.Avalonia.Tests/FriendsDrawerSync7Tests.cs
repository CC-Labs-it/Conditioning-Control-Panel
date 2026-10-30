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
            d.CopyText = text => { copied = text; return Task.FromResult(true); };
            Assert.NotNull(Tagged<Button>(d, "friends-copy-link"));
            Assert.True(await d.CopyLinkAsync());
            Assert.Equal("https://app.cclabs.app/f/7K2Q9ABCDEFG", copied);
            Assert.Equal(Loc.GetF("friends_link_copied_left_hours", 5, 3), d.LinkNoteText);
            Assert.True(await d.CopyLinkAsync());
            Assert.Equal(2, wire.Ops.Count(op => op == "link"));
            Assert.True(await d.NewLinkAsync());
            Assert.Contains("link_revoke", wire.Ops);
            var before = wire.Ops.Count(op => op == "state");
            Assert.Equal(AddResult.Accepted, await d.AddByCodeAsync("https://app.cclabs.app/f/ABCDEFGHJKLM"));
            Assert.Equal(before + 1, wire.Ops.Count(op => op == "state"));
            var box = Tagged<TextBox>(d, "friends-code-box")!;
            box.Text = "https://evil.example/f/7K2Q9ABCDEFG";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("", box.Text);
            box.Text = "https://app.cclabs.app/f/ABCDEFGHJKLM";
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("ABCDEFGHJKLM", box.Text);
            Assert.True(Tagged<Button>(d, "friends-add-go")!.IsEnabled);
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
            var d = new FriendsDrawer(offlineSvc) { CopyText = _ => Task.FromResult(true) };
            Assert.False(await d.CopyLinkAsync());
            Assert.Equal(Loc.Get("friends_link_offline"), d.LinkNoteText);
            d.CopyText = _ => Task.FromResult(false);
            Assert.False(await d.CopyLinkAsync());
            Assert.Equal(Loc.Get("friends_add_try_later"), d.LinkNoteText);
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
}
