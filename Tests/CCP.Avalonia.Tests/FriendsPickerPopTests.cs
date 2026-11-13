using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsPickerPopTests
{
    private static T Find<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().First(c => (c.Tag as string)?.StartsWith(tag) == true);

    [Theory]
    [InlineData("poke", "friends-poke:", 0xFF5FB4)]
    [InlineData("invite", "friends-invite:backroom", 0xB99CFF)]
    [InlineData("watch", "friends-flavour:", 0xFFCF6B)]
    [InlineData("ht", "friends-ht-send", 0xFFCF6B)]
    [InlineData("catalogue", "friends-catalogue:", 0xFFCF6B)]
    public Task PickerPressesPopInWpfColour(string picker, string tag, int rgb) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe, FriendsInviteCodes.CatalogueWatches);
        var oldTime = BeatLoop.Time; var time = new SteppedTime(); BeatLoop.Time = time;
        var (svc, wire) = FriendsDrawerTests.Service();
        Window? host = null;
        try
        {
            settings.MotionLevel = MotionLevel.Full; settings.GentleVisuals = false;
            UiMotionPreference.OsProbe = () => true; UiMotionPreference.CacheStamp = long.MinValue;
            FriendsInviteCodes.CatalogueWatches = () => new[] { ("test", "Test") };
            await svc.RefreshAsync();
            wire.HoldSends = new TaskCompletionSource();
            var drawer = new FriendsDrawer(svc); host = new Window { Content = drawer }; host.Show();
            drawer.OpenPickerFor("u_on", picker == "poke" || picker == "invite" ? picker : "watch");
            if (picker == "ht" || picker == "catalogue")
                Find<Button>(drawer, "friends-watch-tab:" + picker).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (picker == "ht") Find<TextBox>(drawer, "friends-ht-box").Text = "123";
            var pressed = Find<Button>(drawer, tag);
            pressed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var burst = Find<FriendsDrawer.LinkBurst>(drawer, "friends-link-pop");
            Assert.Equal(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), ((ISolidColorBrush)burst.Color).Color);
            time.Advance(TimeSpan.FromMilliseconds(110)); BeatLoop.StepAll();
            Assert.Equal(1.22, ((ScaleTransform)pressed.RenderTransform!).ScaleX, 5);
            host.Close(); host = null;
        }
        finally
        {
            host?.Close(); wire.HoldSends?.SetResult(); svc.Dispose();
            (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe, FriendsInviteCodes.CatalogueWatches) = old;
            UiMotionPreference.CacheStamp = long.MinValue;
            time.Advance(TimeSpan.FromSeconds(2)); BeatLoop.StepAll(); BeatLoop.Time = oldTime;
        }
    });

    [Theory]
    [InlineData(MotionLevel.Off, 0)]
    [InlineData(MotionLevel.Reduced, 6)]
    public Task PickerHonoursMotion(MotionLevel level, int sparks) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe);
        var oldTime = BeatLoop.Time; var time = new SteppedTime(); BeatLoop.Time = time;
        var (svc, wire) = FriendsDrawerTests.Service(); Window? host = null;
        try
        {
            settings.MotionLevel = level; settings.GentleVisuals = false;
            UiMotionPreference.OsProbe = () => true; UiMotionPreference.CacheStamp = long.MinValue;
            await svc.RefreshAsync(); wire.HoldSends = new TaskCompletionSource();
            var drawer = new FriendsDrawer(svc); host = new Window { Content = drawer }; host.Show();
            drawer.OpenPickerFor("u_on", "poke");
            var pressed = Find<Button>(drawer, "friends-poke:");
            var before = pressed.RenderTransform;
            pressed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var burst = drawer.GetLogicalDescendants().OfType<FriendsDrawer.LinkBurst>().SingleOrDefault();
            if (sparks == 0) { Assert.Null(burst); Assert.Same(before, pressed.RenderTransform); }
            else
            {
                Assert.NotNull(burst); Assert.Equal(sparks, burst.Sparks);
                time.Advance(TimeSpan.FromMilliseconds(110)); BeatLoop.StepAll();
                Assert.Equal(1.11, ((ScaleTransform)pressed.RenderTransform!).ScaleX, 5);
            }
        }
        finally
        {
            host?.Close(); wire.HoldSends?.SetResult(); svc.Dispose();
            (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe) = old;
            UiMotionPreference.CacheStamp = long.MinValue;
            time.Advance(TimeSpan.FromSeconds(2)); BeatLoop.StepAll(); BeatLoop.Time = oldTime;
        }
    });
}
