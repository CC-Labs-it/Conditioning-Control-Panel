using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    private sealed class TimerLease : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public Task ChessOpeningCancelsEarlierResultTimer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = FriendsInviteCodes.ChallengeFriend;
        var ready = new TaskCompletionSource<string?>();
        var (svc, _) = Service();
        try
        {
            var d = new FriendsDrawer(svc);
            await svc.RefreshAsync();
            d.Toggle("u_on");
            var timer = new TimerLease();
            d.After = (_, _) => timer;
            await d.PokeAsync("u_on", PokeSet.Shipped[0]);
            FriendsInviteCodes.ChallengeFriend = (_, _) => ready.Task;
            var opening = d.InviteToChessAsync("u_on");
            Assert.True(timer.Disposed);
            Assert.Equal(Loc.Get("friends_invite_chess_opening"), Tagged<TextBlock>(d, "friends-result")?.Text);
            ready.SetResult(null);
            await opening;
        }
        finally { FriendsInviteCodes.ChallengeFriend = old; svc.Dispose(); }
    });

    [Fact]
    public Task FailedChessAfterFoldingSaysWhyOverHost() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = FriendsInviteCodes.ChallengeFriend;
        var ready = new TaskCompletionSource<string?>();
        var (svc, _) = Service();
        var host = new Window();
        try
        {
            host.Show();
            var d = new FriendsDrawer(svc) { OwnerWindow = () => host };
            await svc.RefreshAsync();
            d.OnOpened(); d.Toggle("u_on");
            FriendsInviteCodes.ChallengeFriend = (_, _) => ready.Task;
            var opening = d.InviteToChessAsync("u_on");
            d.OnClosed();
            ready.SetResult(null);
            await opening;
            await Until(() => host.OwnedWindows.OfType<FloatingWord>().Any());
            var word = Assert.Single(host.OwnedWindows.OfType<FloatingWord>());
            Assert.Equal(Loc.Get("friends_invite_chess_failed"), word.GetLogicalDescendants().OfType<TextBlock>().Single().Text);
        }
        finally
        {
            FriendsInviteCodes.ChallengeFriend = old;
            foreach (var w in host.OwnedWindows.ToArray()) w.Close();
            host.Close(); svc.Dispose();
        }
    });

    [Fact]
    public Task RowResultAndPaddingToggleButCardDoesNot() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsDrawer(svc);
        var host = new Window { Width = 500, Height = 700, Content = d };
        try
        {
            await svc.RefreshAsync(); d.OnOpened(); host.Show();
            await d.PokeAsync("u_on", PokeSet.Shipped[0]);
            host.UpdateLayout();
            var row = Tagged<Border>(d, "friends-row:u_on")!;
            var p = row.TranslatePoint(new Point(2, 8), host)!.Value;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(host, p, global::Avalonia.Input.MouseButton.Left);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(host, p, global::Avalonia.Input.MouseButton.Left);
            Assert.Equal("u_on", d.OpenId);
            d.Toggle("u_on"); host.UpdateLayout();
            row = Tagged<Border>(d, "friends-row:u_on")!;
            Click(host, Tagged<TextBlock>(row, "friends-result")!);
            Assert.Equal("u_on", d.OpenId);
            host.UpdateLayout();
            var card = Tagged<StackPanel>(d, "friends-card")!;
            Click(host, card);
            Assert.Equal("u_on", d.OpenId);
        }
        finally { d.OnClosed(); host.Close(); svc.Dispose(); }
    });
}
