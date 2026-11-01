using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class BubblePopEffectsTests
{
    private sealed class AlwaysLucky : Random { public override double NextDouble() => 0; }

    [Fact]
    public Task PopRendersGoldOrPinkAndSamplesThePerformanceGlowGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var randomField = typeof(AmbientBubbleField).GetField(nameof(AmbientBubbleField.Random))!;
        var oldRandom = randomField.GetValue(BubbleOverlay.Field);
        var oldImage = BubbleOverlay.Image;
        var oldSkills = s.UnlockedSkills;
        var (day, paid, perf, auto, glow, suppressed) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday,
            s.PerformanceMode, s.AutoPerformanceMode, s.FlashGlowEnabled, s.SuppressPerkNotifications);
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 240, 220), 1) { Width = 240, Height = 220 };
        try
        {
            randomField.SetValue(BubbleOverlay.Field, new AlwaysLucky());
            BubbleOverlay.Image = null;
            s.SuppressPerkNotifications = true;
            s.AutoPerformanceMode = false;
            s.FlashGlowEnabled = true;
            w.Show();

            (byte b, byte g, byte r, byte a) Pixel(int x, int y)
            {
                using var frame = new RenderTargetBitmap(new PixelSize(240, 220));
                frame.Render((Control)w.Content!);
                var mem = Marshal.AllocHGlobal(4);
                try
                {
                    frame.CopyPixels(new PixelRect(x, y, 1, 1), mem, 4, 4);
                    return (Marshal.ReadByte(mem), Marshal.ReadByte(mem, 1), Marshal.ReadByte(mem, 2), Marshal.ReadByte(mem, 3));
                }
                finally { Marshal.FreeHGlobal(mem); }
            }

            AmbientBubble Pop(params string[] skills)
            {
                s.UnlockedSkills = new List<string>(skills);
                s.AmbientBubbleXpDayKey = "";
                s.AmbientBubbleXpPaidToday = 0;
                BubbleOverlay.Field.Bubbles.Clear();
                var bubble = new AmbientBubble { X = 100, Y = 100, Size = 20 };
                BubbleOverlay.Field.Bubbles.Add(bubble);
                BubbleOverlay.Pop(bubble);
                for (var i = 0; i < 8; i++) bubble.Step();
                return bubble;
            }

            s.PerformanceMode = true;
            var lucky = Pop("lucky_bubbles");
            Assert.Equal(0, lucky.LuckyGlowBlur);
            var gold = Pixel(126, 114); // 8 steps of gravity carry the sparkle below the centre
            Assert.True(gold.r > gold.b && gold.g > gold.b && gold.a > 0);
            Assert.Equal(0, Pixel(140, 110).a);

            s.PerformanceMode = false;
            lucky = Pop("lucky_bubbles");
            Assert.Equal(24, lucky.LuckyGlowBlur);
            Assert.True(Pixel(140, 110).a > 0);

            s.PerformanceMode = true;
            var boosted = Pop("sparkle_boost_1");
            Assert.False(boosted.Lucky);
            var pink = Pixel(126, 114);
            Assert.True(pink.r > pink.g && pink.b > pink.g && pink.a > 0);
        }
        finally
        {
            BubbleOverlay.Field.Bubbles.Clear();
            w.Close();
            BubbleOverlay.Image = oldImage;
            randomField.SetValue(BubbleOverlay.Field, oldRandom);
            s.UnlockedSkills = oldSkills;
            (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday, s.PerformanceMode, s.AutoPerformanceMode,
                s.FlashGlowEnabled, s.SuppressPerkNotifications) = (day, paid, perf, auto, glow, suppressed);
        }
        return Task.CompletedTask;
    });
}
