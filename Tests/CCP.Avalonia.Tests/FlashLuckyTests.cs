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

/// <summary>The lucky glow pulse (WPF FlashService.cs:2002-2016) and
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
            var (clockWas, present, chime, granted, xpWas) = (FlashOverlay.Clock, FlashOverlay.Present, FlashOverlay.LuckyChime,
                PrizeOwnership.IsGranted, CoreProgression.AddXPProvider);
            var clock = new SteppedClock();
            try
            {
                FlashOverlay.Clock = clock;
                BeatLoop.Time = clock;
                FlashOverlay.Present = (_, _, _) => true;
                FlashOverlay.LuckyChime = _ => { };
                CoreProgression.AddXPProvider = (_, _) => { };
                PrizeOwnership.IsGranted = _ => false;
                (s.PerformanceMode, s.FlashRoundedCorners, s.SuppressPerkNotifications, s.FlashMotionStyle) = (false, false, false, FlashMotionStyle.Still);
                s.UnlockedSkills.Clear();
                body(s, clock);
            }
            finally
            {
                FlashOverlay.CloseAll(final: false);
                BeatLoop.Time = beatWas;
                (FlashOverlay.Clock, FlashOverlay.Present, FlashOverlay.LuckyChime, PrizeOwnership.IsGranted,
                    CoreProgression.AddXPProvider) =
                    (clockWas, present, chime, granted, xpWas);
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
    public Task Lucky_glow_pulses_and_stops_on_close() => OnUi((s, clock) =>
    {
        s.FlashGlowEnabled = true;
        s.UnlockedSkills.Add(FlashFxRules.LuckySkillId);
        FlashOverlayWindow? lucky = null;
        for (var i = 0; i < 600 && lucky is null; i++)
        {
            var candidate = ShowOne();
            if (candidate.GlowShadow.Count > 0) lucky = candidate;
            else FlashOverlay.CloseAll(final: false);
        }
        Assert.NotNull(lucky);
        var w = lucky!;
        Assert.True(w.Pulsing);
        var baseBlur = w.GlowShadow[0].Blur;
        clock.Now += TimeSpan.TicksPerSecond * 4 / 10;
        BeatLoop.StepAll();
        Assert.Equal(baseBlur * 1.6, w.GlowShadow[0].Blur, 6);
        Assert.Equal(255, w.GlowShadow[0].Color.A);
        w.Close();
        Assert.False(w.Pulsing);
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
