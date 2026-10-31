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
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)]
    public void BumpSettleIsWpfsOneOscillationElastic(double t, double expected) =>
        Assert.Equal(expected, new FriendsRailChip.WpfElasticOut().Ease(t), 9);

    [Fact]
    public void BumpSettleOvershootsOnceLikeWpf()
    {
        var ease = new FriendsRailChip.WpfElasticOut();
        var sign = Enumerable.Range(1, 99).Select(i => Math.Sign(ease.Ease(i / 100.0) - 1)).Where(v => v != 0).ToArray();
        var crossings = sign.Zip(sign.Skip(1), (a, b) => a != b).Count(c => c);
        Assert.Equal(2, crossings); // WPF Oscillations=1: rise, one overshoot, one dip; Avalonia's ElasticEaseOut rings ~6 times
    }

    [Fact]
    public Task RailTierAndNameFollowOwnerActivation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        chip.Drawer.MeTier = () => 2;
        chip.Drawer.MeName = () => "First";
        var window = new Window { Content = chip }; window.Show();
        try
        {
            Assert.Equal(13, chip.GetVisualDescendants().OfType<Image>().Single(i => Equals(i.Tag, "friends-tier-2")).Height);
            chip.Drawer.MeTier = () => 0;
            chip.Drawer.MeName = () => "Changed";
            // Headless does not transfer activation. Deliver the native callback through the real subscription.
            typeof(WindowBase).GetMethod("HandleActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.StartsWith("Changed\n", (string)ToolTip.GetTip(chip)!);
            Assert.DoesNotContain(chip.GetVisualDescendants().OfType<Image>(), i => Equals(i.Tag, "friends-tier-2"));
        }
        finally { window.Close(); svc.Dispose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RailHoverSnapsAtOffAndAnimatesOtherwise() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var window = new Window { Content = chip }; window.Show();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            chip.Hover(true);
            var scale = (ScaleTransform)chip.Children.OfType<Border>().Single().RenderTransform!;
            Assert.Equal(1.02, scale.ScaleX);
            chip.Hover(false); Assert.Equal(1, scale.ScaleX);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            chip.Hover(true);
            Assert.NotNull(scale.Transitions);
            Assert.Equal(2, scale.Transitions.Count);
            Assert.All(scale.Transitions, t => Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<global::Avalonia.Animation.DoubleTransition>(t).Duration));
        }
        finally { window.Close(); svc.Dispose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RailOnlineCountBumpsAtReducedButNotOffAndCancelsOnDetach() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, wire) = Service();
        var chip = new FriendsRailChip(svc);
        var window = new Window { Content = chip }; window.Show();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            await svc.RefreshAsync();
            Assert.Equal(1, chip.PillCount);
            Assert.Equal(0, chip.ActiveBumps);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            wire.StateBody = State.Replace("\"online\":false", "\"online\":true");
            await svc.RefreshAsync();
            Assert.Equal(2, chip.PillCount);
            Assert.True(chip.ActiveBumps > 0);
            window.Content = null; Assert.Equal(0, chip.ActiveBumps);
        }
        finally { window.Close(); svc.Dispose(); }
    });

    [Fact]
    public Task UnreadBadgeBumpsOnlyOnIncreaseAndHonoursMotionOff() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var feed = new FriendsFeed(() => "u_me");
        var chip = new FriendsRailChip(svc);
        chip.Drawer.FeedSource = () => feed;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var window = new Window { Content = chip }; window.Show();
        try
        {
            Assert.Equal(0, chip.ActiveBumps);
            FriendEvent Ev(string key) => new(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, key, Detail: PokeSet.Shipped[0]);
            Assert.True(feed.Add(Ev("first")));
            Assert.Equal(1, chip.UnreadBadge);
            Assert.Equal(1, chip.ActiveBumps);
            window.Content = null;
            Assert.Equal(0, chip.ActiveBumps);
            window.Content = chip;
            chip.UpdateBadge();
            Assert.Equal(0, chip.ActiveBumps); // rebind and equal count must not bump
            feed.MarkAllRead();
            Assert.Equal(0, chip.ActiveBumps); // a decrease must not bump
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            Assert.True(feed.Add(Ev("second")));
            Assert.Equal(1, chip.UnreadBadge);
            Assert.Equal(0, chip.ActiveBumps);
            await Task.CompletedTask;
        }
        finally { window.Close(); svc.Dispose(); }
    });
}
