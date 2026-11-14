using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Restack b213 (#4069): WPF Bubble.SetLucky (BubbleService.cs:1111, :4452-4472). A paid lucky pop
/// with perk visuals gets a gold glow capped by the performance tier and none on the Performance tier;
/// the shadow is drawn around the sprite and fades with it.</summary>
public sealed class BubbleLuckyGlowTests
{
    private sealed class AlwaysLucky : Random { public override double NextDouble() => 0; }

    [Fact]
    public Task LuckyPopGlowFollowsTierAndPerkVisualsAndDrawsAroundTheSprite() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var randomField = typeof(AmbientBubbleField).GetField(nameof(AmbientBubbleField.Random))!;
        var oldRandom = randomField.GetValue(BubbleOverlay.Field);
        var oldImage = BubbleOverlay.Image;
        var oldProc = FlashOverlay.LuckyProc;
        var oldSkills = s.UnlockedSkills;
        var old = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday, s.PerformanceMode, s.AutoPerformanceMode, s.PerkEffects, s.FlashGlowEnabled);
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 300, 300), 1);
        try
        {
            randomField.SetValue(BubbleOverlay.Field, new AlwaysLucky());
            FlashOverlay.LuckyProc = (_, _) => { };   // no toast window from a test
            BubbleOverlay.Image = null;   // the fallback circle: a known sprite edge
            s.AutoPerformanceMode = false;
            s.FlashGlowEnabled = false;   // no boost sparkles besides the lucky ones
            AmbientBubble Pop(string[] skills, bool performance, PerkEffectsMode perk)
            {
                s.UnlockedSkills = new List<string>(skills);
                s.AmbientBubbleXpDayKey = ""; s.AmbientBubbleXpPaidToday = 0;
                s.PerformanceMode = performance; s.PerkEffects = perk;
                BubbleOverlay.Field.Bubbles.Clear();
                var b = new AmbientBubble { X = 100, Y = 100, Size = 100, Screen = 0 };
                BubbleOverlay.Field.Bubbles.Add(b);
                BubbleOverlay.Pop(b);
                return b;
            }
            Assert.Equal(0, Pop(Array.Empty<string>(), false, PerkEffectsMode.Full).LuckyGlowBlur);   // not lucky
            Assert.Equal(0, Pop(new[] { "lucky_bubbles" }, true, PerkEffectsMode.Full).LuckyGlowBlur);   // Performance tier skips the blur
            Assert.Equal(0, Pop(new[] { "lucky_bubbles" }, false, PerkEffectsMode.Off).LuckyGlowBlur);   // perk visuals off: a plain pop
            var lucky = Pop(new[] { "lucky_bubbles" }, false, PerkEffectsMode.Full);
            Assert.True(lucky.Lucky);
            Assert.Equal(Math.Min(50, global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.MaxGlowBlurRadius(
                global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.CurrentTier)), lucky.LuckyGlowBlur);

            // Just outside the fallback circle (radius 45 around 150,150): the glow paints gold there, none without it.
            for (var i = 0; i < lucky.Sparkles.Length; i++) lucky.Sparkles[i] = default;
            byte AlphaAt(int x, int y)
            {
                var layer = (Control)w.Content!;
                layer.Measure(new Size(300, 300)); layer.Arrange(new Rect(0, 0, 300, 300));
                using var image = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(300, 300));
                image.Render(layer);
                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
                try
                {
                    image.CopyPixels(new PixelRect(x, y, 1, 1), buf, 4, 4);
                    return (byte)(unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf)) >> 24);
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
            }
            lucky.Scale = 1; lucky.Angle = 0; lucky.Fade = 1;
            var lit = AlphaAt(150, 150 - 52);
            Assert.True(lit > 0, "no glow outside the sprite");
            lucky.Fade = 0.3;
            Assert.True(AlphaAt(150, 150 - 52) < lit, "the glow does not fade with the bubble");
            lucky.Fade = 1; lucky.LuckyGlowBlur = 0;
            Assert.Equal(0, AlphaAt(150, 150 - 52));
        }
        finally
        {
            BubbleOverlay.Field.Bubbles.Clear();
            w.Close();
            BubbleOverlay.Image = oldImage;
            FlashOverlay.LuckyProc = oldProc;
            randomField.SetValue(BubbleOverlay.Field, oldRandom);
            s.UnlockedSkills = oldSkills;
            (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday, s.PerformanceMode, s.AutoPerformanceMode, s.PerkEffects, s.FlashGlowEnabled) = old;
        }
        return Task.CompletedTask;
    });
}
