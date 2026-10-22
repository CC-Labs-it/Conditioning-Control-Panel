using System;
using ConditioningControlPanel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using CCP.Avalonia.Testing;
using Xunit;
using ConditioningControlPanel.Avalonia.Platform;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    private static void ChipApp()
    {
        if (Application.Current == null)
            AppBuilder.Configure<AvApp>().UseSkia().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public Task RailUnreadTracksFeedReadAccountAndDetach() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        string? account = "u_me";
        var feed = new FriendsFeed(() => account);
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc, feed);
        chip.Drawer.MeName = () => "A long full account name";
        var window = new Window { Content = chip }; window.Show();
        try
        {
            await svc.RefreshAsync();
            Assert.Equal(1, chip.PillCount);
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "first"));
            Assert.Equal(1, chip.UnreadBadge);
            Assert.Contains("A long full account name", ToolTip.GetTip(chip)!.ToString());
            for (var n = 0; n < 12; n++) feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "poke" + n));
            Assert.Equal(10, chip.UnreadBadge); // displayed 9+
            Assert.Equal(1, chip.PillCount); // independent badges
            feed.MarkAllRead(); Assert.Equal(0, chip.UnreadBadge);
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "next"));
            account = "different"; chip.UpdatePill(); Assert.Equal(0, chip.UnreadBadge);
            window.Content = null;
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "detached"));
            Assert.Equal(0, chip.UnreadBadge);
            Assert.Equal(0, chip.ActiveBumps);
        }
        finally { window.Close(); svc.Dispose(); }
    });

    [Fact]
    public Task RailTierMotionAndPopupFollowOwnerLifecycle() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var old = CoreSettings.Current.MotionLevel;
        var (svc, _) = Service();
        var feed = new FriendsFeed(() => "u_me");
        var chip = new FriendsRailChip(svc, feed);
        chip.Drawer.MeTier = () => 2;
        var window = new Window { Content = chip, Width = 400, Height = 400 }; window.Show();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            chip.RefreshFace();
            Assert.Equal(13, chip.GetVisualDescendants().OfType<Image>().Single(i => Equals(i.Tag, "friends-tier-2")).Height);
            chip.Drawer.MeTier = () => 0; chip.RefreshFace();
            Assert.DoesNotContain(chip.GetVisualDescendants().OfType<Image>(), i => Equals(i.Tag, "friends-tier-2"));
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "off"));
            Assert.Equal(0, chip.ActiveBumps);
            chip.Hover(true);
            var face = chip.Children.OfType<Border>().Single();
            Assert.Equal(1.02, ((ScaleTransform)face.RenderTransform!).ScaleX);
            chip.Hover(false); Assert.Equal(1, ((ScaleTransform)face.RenderTransform!).ScaleX);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "reduced"));
            Assert.True(chip.ActiveBumps > 0);
            chip.Toggle(); Assert.True(chip.IsOpen);
            if (TopLevel.GetTopLevel(chip.Drawer) is WindowBase popup) Assert.False(popup.Topmost);
            window.WindowState = WindowState.Minimized;
            Assert.False(chip.IsOpen);
            window.Content = null; Assert.Equal(0, chip.ActiveBumps);
            await Task.CompletedTask;
        }
        finally { window.Close(); svc.Dispose(); CoreSettings.Current.MotionLevel = old; }
    });
    [Fact]
    public Task RailCodeClickActivatesOwnerAndFocusesBox() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Width = 500, Height = 600, Content = chip };
        var other = new Window { Width = 100, Height = 100 };
        owner.Show();
        try
        {
            await svc.RefreshAsync();
            chip.Toggle();
            Click(chip.Drawer, "friends-add-open");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var box = Tagged<TextBox>(chip.Drawer, "friends-code-box")!;
            Assert.NotNull(box);
            other.Show(); other.Activate();
            Click(TopLevel.GetTopLevel(box)!, box);
            Assert.True(owner.IsActive);
            Assert.True(box.IsFocused);
            Assert.True(chip.IsOpen);
        }
        finally { other.Close(); owner.Close(); svc.Dispose(); }
    });

}
