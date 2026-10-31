using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia;
using Avalonia.Headless;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class LuckyToastTests
{
    [Fact]
    public Task LuckyToastIsOneReusedWindowThatHoldsThenHidesAndSkipsUnderGamesOrOptOut() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (suppressedWas, probeWas) = (s.SuppressPerkNotifications, LuckyToast.GameOwnsScreen);
        try
        {
            s.SuppressPerkNotifications = false;
            LuckyToast.GameOwnsScreen = () => false;
            LuckyToast.Proc("Lucky Flash", 10);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(LuckyToast.Popup);
            var popup = LuckyToast.Popup!;
            Assert.True(popup.IsVisible);
            Assert.True(LuckyToast.Holding);
            Assert.Equal(TimeSpan.FromSeconds(3), LuckyToast.HoldInterval);
            var fade = Assert.IsType<DoubleTransition>(Assert.Single(popup.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(200), fade.Duration);
            if (popup.Screens.Primary is { } screen)
                Assert.Equal(screen.WorkingArea.Y + (int)(10 * screen.Scaling), popup.Position.Y);
            var texts = popup.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Equal(new[] { Loc.Get("lucky_toast_title"), Loc.GetF("lucky_toast_xp", 10) }, texts);
            var title = popup.GetLogicalDescendants().OfType<TextBlock>().First();
            Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00), ((ISolidColorBrush)title.Foreground!).Color);   // "Flash" = gold

            LuckyToast.Proc("Lucky Bubble", 20);                   // a proc while up refreshes the same window
            Dispatcher.UIThread.RunJobs();
            Assert.Same(popup, LuckyToast.Popup);
            Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), ((ISolidColorBrush)title.Foreground!).Color);
            Assert.Contains(Loc.GetF("lucky_toast_xp", 20), popup.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

            LuckyToast.HoldElapsed();                              // 3 s after the last proc: fade, then hide
            Assert.Equal(TimeSpan.FromMilliseconds(300), fade.Duration);
            LuckyToast.Proc("Lucky Bubble", 20);
            Dispatcher.UIThread.RunJobs();
            LuckyToast.FadeOutDone();
            Assert.True(popup.IsVisible); // proc during fade cancels the pending hide
            LuckyToast.HoldElapsed();
            LuckyToast.FadeOutDone();
            Assert.False(popup.IsVisible);
            LuckyToast.Proc("Lucky Flash", 10);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(popup, LuckyToast.Popup); // hidden windows are reused too
            LuckyToast.HoldElapsed();
            LuckyToast.FadeOutDone();

            LuckyToast.GameOwnsScreen = () => true;                // a game owns the screen: the toast stays down
            LuckyToast.Proc("Lucky Flash", 10);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsVisible);
            LuckyToast.GameOwnsScreen = () => false;
            s.SuppressPerkNotifications = true;                    // perk opt-out
            LuckyToast.Proc("Lucky Flash", 10);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsVisible);
            s.SuppressPerkNotifications = false;
            LuckyToast.Proc("Lucky Flash", 10);
            LuckyToast.Close(); // cancels queued work as well as timers
            Dispatcher.UIThread.RunJobs();
            Assert.Null(LuckyToast.Popup);
            Assert.False(LuckyToast.Holding);
        }
        finally
        {
            LuckyToast.Close();
            s.SuppressPerkNotifications = suppressedWas;
            LuckyToast.GameOwnsScreen = probeWas;
        }
        return Task.CompletedTask;
    });
}
