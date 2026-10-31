using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FlashGapTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => 1000;
    }
    private static Task OnUi(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        try { body(); }
        finally { LuckyFlashToast.CloseActive(); FlashOverlay.CloseAll(final: false); }
        return Task.CompletedTask;
    });

    private sealed class LuckyRandom : Random { public override double NextDouble() => 0; }

    [Fact]
    public Task Flash_roll_routes_the_mod_aware_proc_and_hydra_does_not_roll() => OnUi(() =>
    {
        CoreSettings.Current.UnlockedSkills.Add("lucky_bimbo");
        FlashOverlay.Rng = new LuckyRandom();
        FlashOverlay.Present = (_, _, _) => false;
        int calls = 0;
        FlashOverlay.LuckyProc = (label, multiplier) => { Assert.False(string.IsNullOrEmpty(label)); Assert.Equal(10, multiplier); calls++; };
        var host = new Window();
        try
        {
            for (int generation = 0; generation < 2; generation++)
                FlashOverlay.Show(new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul),
                    new PixelRect(0, 0, 2, 2), "roll.png", host.Screens.All[0], 1, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, false, generation);
            Assert.Equal(1, calls);
        }
        finally { host.Close(); }
    });

    [Fact]
    public Task Toast_reuses_refreshes_hides_and_reopens_with_shared_labels() => OnUi(() =>
    {
        var clock = new Clock();
        FlashOverlay.Clock = clock;
        LuckyFlashToast.ScreenOwned = () => false;
        LuckyFlashToast.Prepare = (_, _) => true;
        CoreSettings.Current.SuppressPerkNotifications = true;
        FlashOverlay.LuckyProc("Lucky Flash", 10);
        Assert.Null(LuckyFlashToast.Active);
        CoreSettings.Current.SuppressPerkNotifications = false;
        LuckyFlashToast.ScreenOwned = () => true;
        FlashOverlay.LuckyProc("Lucky Flash", 10);
        Assert.Null(LuckyFlashToast.Active);
        LuckyFlashToast.ScreenOwned = () => false;
        FlashOverlay.LuckyProc("Lucky Flash", 10);
        var w = Assert.IsType<LuckyFlashToast>(LuckyFlashToast.Active);
        Assert.Equal(0, w.Alpha);
        clock.Now = 200;
        w.Tick();
        Assert.Equal(1, w.Alpha);
        clock.Now = 3100;
        w.Tick();
        Assert.InRange(w.Alpha, 0.6, 0.7);
        BubbleOverlay.Field.LuckyProc!(20);
        Assert.Same(w, LuckyFlashToast.Active);
        Assert.Equal(1, w.Alpha);
        Assert.Equal("20x XP", w.MultiplierText);
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), Assert.IsType<SolidColorBrush>(w.Accent).Color);
        clock.Now = 6500;
        w.Tick();
        Assert.False(w.IsVisible);
        FlashOverlay.LuckyProc("Mod Flash", 10);
        Assert.Same(w, LuckyFlashToast.Active);
        Assert.True(w.IsVisible);
        Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0), Assert.IsType<SolidColorBrush>(w.Accent).Color);
        FlashOverlay.CloseAll(final: false);
        Assert.Null(LuckyFlashToast.Active);
    });

    [Fact]
    public Task Scheduled_frames_recheck_the_mode_cap_and_resume_after_a_full_frame() => OnUi(() =>
    {
        var s = CoreSettings.Current;
        s.FlashClickable = s.FlashStayUntilPopped = true;
        var ticks = new List<Action>();
        FlashOverlay.RunLater = (a, _) => ticks.Add(a);
        int shown = 0;
        FlashOverlay.Present = (_, _, _) => { shown++; return true; };
        CoreProgression.AddXPProvider = (_, _) => { };
        var host = new Window();
        using var picture = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var placeholder = new FlashOverlayWindow(picture);
        try
        {
            for (int i = 0; i < 30; i++) FlashOverlay.Active.Add((placeholder, default));
            var frames = new List<(Bitmap, PixelRect, string, Screen)>();
            for (int i = 0; i < 3; i++) frames.Add((new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul), new PixelRect(0, 0, 2, 2), "queued.png", host.Screens.All[0]));
            bool done = false;
            FlashOverlay.Schedule(frames, 0, 100, FlashOverlay.Generation, 1, TimeSpan.Zero, TimeSpan.FromMinutes(1),
                false, true, 0, () => done = true);
            ticks[0]();
            Assert.Equal(1, shown); // staying mode: room below 40
            s.FlashStayUntilPopped = false;
            ticks[1]();
            Assert.Equal(1, shown); // live cap is now 30
            s.FlashStayUntilPopped = true;
            ticks[2]();
            Assert.Equal(2, shown); // a skipped frame must not refuse the rest
            Assert.True(done);
        }
        finally
        {
            FlashOverlay.Active.RemoveAll(e => e.Window == placeholder);
            placeholder.Close();
            host.Close();
        }
    });
}
