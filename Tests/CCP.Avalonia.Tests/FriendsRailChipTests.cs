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
    [Fact]
    public Task RailOpenAndClosePlayTheDrawerSounds() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var window = new Window { Content = chip, Width = 400, Height = 400 }; window.Show();
        var oldPlay = CoreAudio.PlayOneShotProvider;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var tags = new System.Collections.Generic.List<string>();
        try
        {
            CoreSettings.Current.MasterVolume = 80;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            FriendsLanding.SfxGate = new FriendsSfxGate();
            chip.Toggle(); Assert.True(chip.IsOpen);
            FriendsLanding.SfxGate = new FriendsSfxGate(); // the 130 ms gap is real time; a fresh gate stands in for it
            chip.Toggle(); Assert.False(chip.IsOpen);
            Assert.Equal(new[] { "friends-open", "friends-close" }, tags);
            await Task.CompletedTask;
        }
        finally { CoreAudio.PlayOneShotProvider = oldPlay; CoreSettings.Current.MasterVolume = oldVolume; FriendsLanding.SfxGate = new FriendsSfxGate(); window.Close(); svc.Dispose(); }
    });

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
    public Task RailTierMotionAndPopupFollowOwnerLifecycle() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var old = CoreSettings.Current.MotionLevel;
        var (svc, wire) = Service();
        var chip = new FriendsRailChip(svc);
        chip.Drawer.MeTier = () => 2;
        var window = new Window { Content = chip, Width = 400, Height = 400 }; window.Show();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            chip.RefreshFace();
            Assert.Equal(13, chip.GetVisualDescendants().OfType<Image>().Single(i => Equals(i.Tag, "friends-tier-2")).Height);
            chip.Drawer.MeTier = () => 0; chip.RefreshFace();
            Assert.DoesNotContain(chip.GetVisualDescendants().OfType<Image>(), i => Equals(i.Tag, "friends-tier-2"));
            await svc.RefreshAsync(); // Mia online: 0 -> 1, but motion is off
            Assert.Equal(1, chip.PillCount);
            Assert.Equal(0, chip.ActiveBumps);
            chip.Hover(true);
            var face = chip.Children.OfType<Border>().Single();
            Assert.Equal(1.02, ((ScaleTransform)face.RenderTransform!).ScaleX);
            chip.Hover(false); Assert.Equal(1, ((ScaleTransform)face.RenderTransform!).ScaleX);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            wire.StateBody = State.Replace("\"online\":false", "\"online\":true"); // Zed comes online too
            await svc.RefreshAsync();
            Assert.Equal(2, chip.PillCount);
            Assert.True(chip.ActiveBumps > 0); // the online pill bumps (WPF BumpCount)
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
