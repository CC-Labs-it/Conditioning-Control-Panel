using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The lucky flash beyond its XP (WPF FlashService.cs:1995-2008, MainWindow.OnLuckyProc) and
/// the rounded corners (FlashCorners via FlashService.cs:1956, 2020, ApplyCornerClip).</summary>
public sealed class FlashLuckyTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static Task OnUi(Action<AppSettings, SteppedClock> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().WithInterFont()
                    .With(AppFonts.Options())
                    .ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.FlashGlowEnabled, s.PerformanceMode, s.FlashRoundedCorners, s.SuppressPerkNotifications, s.FlashMotionStyle,
                Skills: s.UnlockedSkills.ToList());
            var beatWas = BeatLoop.Time;
            var (clockWas, present, chime, proc, granted, game, xpWas) = (FlashOverlay.Clock, FlashOverlay.Present, FlashOverlay.LuckyChime,
                FlashOverlay.LuckyProc, PrizeOwnership.IsGranted, LuckyToastWindow.GameOwnsScreen, CoreProgression.AddXPProvider);
            var clock = new SteppedClock();
            try
            {
                FlashOverlay.Clock = clock;
                BeatLoop.Time = clock;
                FlashOverlay.Present = (_, _, _) => true;
                FlashOverlay.LuckyChime = _ => { };
                FlashOverlay.LuckyProc = (_, _) => { };
                CoreProgression.AddXPProvider = (_, _) => { };
                PrizeOwnership.IsGranted = _ => false;
                (s.PerformanceMode, s.FlashRoundedCorners, s.SuppressPerkNotifications, s.FlashMotionStyle) = (false, false, false, FlashMotionStyle.Still);
                s.UnlockedSkills.Clear();
                body(s, clock);
            }
            finally
            {
                FlashOverlay.CloseAll(final: false);
                LuckyToastWindow.CloseLive();
                BeatLoop.Time = beatWas;
                (FlashOverlay.Clock, FlashOverlay.Present, FlashOverlay.LuckyChime, FlashOverlay.LuckyProc, PrizeOwnership.IsGranted,
                    LuckyToastWindow.GameOwnsScreen, CoreProgression.AddXPProvider) =
                    (clockWas, present, chime, proc, granted, game, xpWas);
                (s.FlashGlowEnabled, s.PerformanceMode, s.FlashRoundedCorners, s.SuppressPerkNotifications, s.FlashMotionStyle) =
                    (saved.FlashGlowEnabled, saved.PerformanceMode, saved.FlashRoundedCorners, saved.SuppressPerkNotifications, saved.FlashMotionStyle);
                s.UnlockedSkills.Clear();
                s.UnlockedSkills.AddRange(saved.Skills);
            }
            return Task.CompletedTask;
        });

    private static Bitmap Picture() => new(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));

    private static Screen? _screen;

    private static FlashOverlayWindow ShowOne()
    {
        if (_screen is null)
        {
            var probe = new Window();
            _screen = probe.Screens.All[0];
            probe.Close();
        }
        var screen = _screen;
        Assert.True(FlashOverlay.Show(Picture(), new PixelRect(100, 100, 400, 300), "a.png", screen, 1, TimeSpan.Zero,
            TimeSpan.FromSeconds(6), clickable: false, ambient: false, hydraGeneration: 0));
        return FlashOverlay.Active[^1].Window;
    }

    [Fact]
    public Task A_lucky_flash_raises_the_proc_and_pulses_its_gold_glow_the_others_hold_still() => OnUi((s, clock) =>
    {
        var procs = new List<(string Label, int Multiplier)>();
        FlashOverlay.LuckyProc = (l, m) => procs.Add((l, m));
        s.FlashGlowEnabled = true;
        s.UnlockedSkills.Add(FlashFxRules.LuckySkillId);
        FlashOverlayWindow? lucky = null;
        for (var i = 0; i < 600 && lucky is null; i++)   // 5% a roll: 0.95^600 ~ 4e-14 to miss
        {
            var w = ShowOne();
            if (procs.Count > 0) lucky = w;
            else
            {
                Assert.False(w.Pulsing);   // no glow without sparkle boost, nothing pulses
                FlashOverlay.CloseAll(final: false);
            }
        }
        Assert.NotNull(lucky);
        Assert.Equal(FlashFxRules.LuckyMultiplier, Assert.Single(procs).Multiplier);
        Assert.True(lucky!.Pulsing);
        var baseBlur = lucky.GlowShadow[0].Blur;   // 60, capped by the tier
        clock.Now += TimeSpan.TicksPerSecond * 4 / 10;   // the top of the 400 ms swing
        BeatLoop.StepAll();
        Assert.Equal(baseBlur * 1.6, lucky.GlowShadow[0].Blur, 6);
        Assert.Equal(255, lucky.GlowShadow[0].Color.A);
        Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(lucky.GlowShadow[0].Color.R, lucky.GlowShadow[0].Color.G, lucky.GlowShadow[0].Color.B));
        lucky.Close();
        Assert.False(lucky.Pulsing);
    });

    [Fact]
    public Task The_toast_shows_once_refreshes_on_a_second_proc_and_hides_after_its_hold() => OnUi((s, _) =>
    {
        var gameUp = false;
        LuckyToastWindow.GameOwnsScreen = () => gameUp;
        LuckyToastWindow.OnLuckyProc("Lucky Flash", 10);
        Dispatcher.UIThread.RunJobs();
        var toast = LuckyToastWindow.Live!;
        Assert.True(toast.IsVisible);
        Assert.Equal("10x XP!", toast.MultiplierText);
        Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00), ((ISolidColorBrush)toast.Accent!).Color);

        LuckyToastWindow.OnLuckyProc("Lucky Bubble", 20);   // while up: the same window, refreshed
        Dispatcher.UIThread.RunJobs();
        Assert.Same(toast, LuckyToastWindow.Live);
        Assert.Equal("20x XP!", toast.MultiplierText);
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), ((ISolidColorBrush)toast.Accent!).Color);
        Assert.True(toast.Holding);

        toast.HoldElapsed();
        toast.CompleteFade();
        Assert.False(toast.IsVisible);

        gameUp = true;   // a game surface owns the screen: no toast
        LuckyToastWindow.OnLuckyProc("Lucky Flash", 10);
        Dispatcher.UIThread.RunJobs();
        Assert.False(toast.IsVisible);

        gameUp = false;
        s.SuppressPerkNotifications = true;   // perk notices off
        LuckyToastWindow.OnLuckyProc("Lucky Flash", 10);
        Dispatcher.UIThread.RunJobs();
        Assert.False(toast.IsVisible);
    });

    [Fact]
    public Task Rounded_corners_clip_the_picture_and_a_glow_card_rounds_at_12() => OnUi((s, _) =>
    {
        s.FlashGlowEnabled = false;
        var plain = ShowOne();
        Assert.Equal(0, plain.CornerRadiusDip);
        Assert.Null(plain.PictureClip);

        s.FlashRoundedCorners = true;   // counts only with a Flashes v2 grant
        Assert.Equal(0, ShowOne().CornerRadiusDip);
        PrizeOwnership.IsGranted = id => id == PrizeOwnership.FlashDriftBounce;
        var round = ShowOne();
        var k = round.Screens.All[0].Scaling;
        Assert.Equal(FlashCorners.Resolve(true, true, false, 300 / k, 1.0), round.CornerRadiusDip, 6);
        round.Width = round.Height = 300 / k;   // square, so the 512x512 picture fills it: no letterbox at the corner
        var frame = round.CaptureRenderedFrame()!;
        using (var fb = frame.Lock())
        {
            var px = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(fb.Address + fb.RowBytes + 4, px, 0, 4);
            Assert.Equal(0, px[3]);   // the corner is cut away
            System.Runtime.InteropServices.Marshal.Copy(fb.Address + (fb.Size.Height / 2) * fb.RowBytes + (fb.Size.Width / 2) * 4, px, 0, 4);
            Assert.Equal(255, px[3]);   // the picture itself is there
        }

        s.FlashRoundedCorners = false;
        s.FlashGlowEnabled = true;
        s.UnlockedSkills.Add("sparkle_boost_1");   // the glow card: 12 DIPs
        var glow = ShowOne();
        Assert.Equal(FlashCorners.GlowBaseDip, glow.CornerRadiusDip);
    });
}
