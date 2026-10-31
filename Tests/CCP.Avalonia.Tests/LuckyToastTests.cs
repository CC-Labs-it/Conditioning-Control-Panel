using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LuckyToastTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => 1000;
    }

    [Fact]
    public Task Shared_toast_routes_both_procs_refreshes_during_fade_and_hides_for_reuse() => Ui(() =>
    {
        var clock = new Clock();
        LuckyToastWindow.Clock = clock;
        FlashOverlay.LuckyChime = _ => { };
        FlashOverlay.Paid("test.png", CoreSettings.Current, 0, 10);
        Dispatcher.UIThread.RunJobs();
        var w = Assert.IsType<LuckyToastWindow>(LuckyToastWindow.Current);
        Assert.True(w.IsVisible);
        w.UpdateLayout();
        var screen = w.Screens.Primary!;
        Assert.Equal(screen.WorkingArea.Y + (int)(40 * screen.Scaling), w.Position.Y);
        Assert.Equal(screen.WorkingArea.X + (screen.WorkingArea.Width - (int)(w.Bounds.Width * screen.Scaling)) / 2, w.Position.X);
        Assert.Equal("10x XP", w.Multiplier.Text);
        var card = (Border)w.Content!;
        Assert.Equal(Colors.Gold, ((ISolidColorBrush)card.BorderBrush!).Color);
        clock.Now = 200; w.Tick(); Assert.Equal(1, w.Opacity);
        clock.Now = 3000; w.Tick();
        clock.Now = 3150; w.Tick(); Assert.Equal(0.5, w.Opacity, 3);
        // WPF SkillTreeService.cs:222: bubble notification is independent of the XP bucket.
        BubbleOverlay.Field.LuckyProc!(20);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(w, LuckyToastWindow.Current);
        Assert.Equal("20x XP", w.Multiplier.Text);
        Assert.Equal(Colors.HotPink, ((ISolidColorBrush)card.BorderBrush!).Color);
        Assert.Equal(1, w.Opacity);
        clock.Now = 6149; w.Tick(); Assert.True(w.IsVisible);
        clock.Now = 6150; w.Tick();
        clock.Now = 6450; w.Tick(); Assert.False(w.IsVisible);
        LuckyToastWindow.ShowProc(true, 10);
        Assert.Same(w, LuckyToastWindow.Current);
        Assert.True(w.IsVisible);
    });

    [Fact]
    public Task Suppression_game_ownership_and_shutdown_drop_notifications() => Ui(() =>
    {
        CoreSettings.Current.SuppressPerkNotifications = true;
        LuckyToastWindow.ShowProc(true, 10);
        Assert.Null(LuckyToastWindow.Current);
        CoreSettings.Current.SuppressPerkNotifications = false;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("test", _ => { }, () => true) };
        LuckyToastWindow.Post(false, 20);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(LuckyToastWindow.Current);
        PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
        LuckyToastWindow.Post(true, 10);
        LuckyToastWindow.CloseToast();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(LuckyToastWindow.Current);
    });

    private static Task Ui(Action action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var saved = (CoreSettings.Current.SuppressPerkNotifications, LuckyToastWindow.Clock,
            PanicSurfaces.All, FlashOverlay.LuckyChime, CoreProgression.AddXPProvider);
        try
        {
            CoreSettings.Current.SuppressPerkNotifications = false;
            PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
            CoreProgression.AddXPProvider = (_, _) => { };
            action();
        }
        finally
        {
            LuckyToastWindow.CloseToast();
            (CoreSettings.Current.SuppressPerkNotifications, LuckyToastWindow.Clock,
                PanicSurfaces.All, FlashOverlay.LuckyChime, CoreProgression.AddXPProvider) = saved;
        }
        return Task.CompletedTask;
    });
}
