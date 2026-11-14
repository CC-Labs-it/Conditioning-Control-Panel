using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Bubble hit-area press: a press inside a bubble pops it and pays the ambient 5 XP
/// from the daily bucket; a press beside it pops nothing.</summary>
public sealed class BubbleOverlayTests
{
    /// <summary>WPF BubbleService.cs:221 LoadBubbleImage: each Start re-resolves bubble.png through the
    /// active mod, so after a mod switch the next start draws the new mod's sprite, not the cached one.</summary>
    [Fact]
    public async Task Each_start_reads_the_active_mods_bubble_sprite()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = System.IO.Directory.CreateTempSubdirectory("ccp-bubble-");
            var png = System.IO.Path.Combine(dir.FullName, "pill.png");
            using (var bmp = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(7, 3), new Vector(96, 96)))
                bmp.Save(png);
            var (previous, oldImage) = (CoreModArt.OverridePathProvider, BubbleOverlay.Image);
            try
            {
                CoreModArt.OverridePathProvider = _ => null;              // the default mod's sprite
                BubbleOverlay.LoadBubbleImage();
                Assert.NotEqual(7, BubbleOverlay.Image!.PixelSize.Width);
                CoreModArt.OverridePathProvider = p => p == "bubble.png" ? png : null;   // switched mod
                BubbleOverlay.LoadBubbleImage();
                Assert.Equal(7, BubbleOverlay.Image!.PixelSize.Width);
            }
            finally
            {
                CoreModArt.OverridePathProvider = previous;
                BubbleOverlay.Image = oldImage;
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Press_on_a_bubble_pops_it_and_pays_the_bucket()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (dayKey, paid) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);   // process-global: put it back
            s.AmbientBubbleXpDayKey = "";   // fresh day
            var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1280, 720), 1) { Width = 1280, Height = 720 };
            var card = new ConditioningControlPanel.Avalonia.Views.Features.BubblePopFeatureControl();
            var host = new global::Avalonia.Controls.Window { Content = card };
            host.Show();
            var line = card.FindControl<global::Avalonia.Controls.TextBlock>("TxtAmbientXpBudget")!;
            var b = new AmbientBubble { Screen = 0, X = 400, Y = 300, Size = 200, Clickable = true };
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Bubbles.Add(b);
            try
            {
                w.Show();
                w.MouseDown(new Point(300, 400), MouseButton.Left);   // beside it
                Assert.False(b.Popping);
                w.MouseDown(new Point(500, 400), MouseButton.Right);  // its centre; right pops too
                Assert.True(b.Popping);
                Assert.Equal(5, AmbientBubbleXp.PaidToday(s));
                Assert.Contains("5/300", line.Text);   // the card's N/300 line follows the pop
            }
            finally
            {
                BubbleOverlay.Field.Bubbles.Clear(); w.Close(); host.Close();
                s.AmbientBubbleXpDayKey = dayKey; s.AmbientBubbleXpPaidToday = paid;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Ambient_gif_rain_pop_pays_and_fires_once()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current;
            var oldDay = s.AmbientBubbleXpDayKey;
            var oldPaid = s.AmbientBubbleXpPaidToday;
            var oldFirer = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
            var calls = 0;
            var payload = new ConditioningControlPanel.Services.Chaos.GifCascadePayload { Ambient = true };
            var b = new AmbientBubble { Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec { Payload = payload } };
            try
            {
                s.AmbientBubbleXpDayKey = "";
                ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = p => { if (p == payload) calls++; };
                BubbleOverlay.Pop(b);
                BubbleOverlay.Pop(b);
                Assert.Equal(1, calls);
                Assert.True(b.Popping);
                Assert.Equal(5, AmbientBubbleXp.PaidToday(s));
            }
            finally
            {
                ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = oldFirer;
                s.AmbientBubbleXpDayKey = oldDay;
                s.AmbientBubbleXpPaidToday = oldPaid;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Gif_rain_uses_its_shipped_variant_art_and_other_bubbles_use_the_generic_art()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var oldImage = BubbleOverlay.Image;
            var oldRainImage = BubbleOverlay.GifRainImage;
            try
            {
                BubbleOverlay.LoadBubbleImage();
                Assert.NotNull(BubbleOverlay.GifRainImage);
                var rain = new AmbientBubble { Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec { VariantId = "htlink" } };
                Assert.Same(BubbleOverlay.GifRainImage, BubbleOverlay.SpriteFor(rain));
                Assert.Same(BubbleOverlay.Image, BubbleOverlay.SpriteFor(new AmbientBubble()));
                BubbleOverlay.GifRainImage = null;
                Assert.Same(BubbleOverlay.Image, BubbleOverlay.SpriteFor(rain));
            }
            finally { BubbleOverlay.Image = oldImage; BubbleOverlay.GifRainImage = oldRainImage; }
            return Task.CompletedTask;
        });
    }

    /// <summary>A dashboard Gif Rain is not part of a Chaos run: the run neither counts nor clears it.</summary>
    [Fact]
    public async Task Chaos_run_counts_and_clears_only_its_own_bubbles()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var saved = BubbleOverlay.Field.Bubbles.ToArray();
            var held = BubbleOverlay.Field.HoldChaos;
            var trigger = new AmbientBubble { Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec
                { VariantId = "htlink", Payload = new ConditioningControlPanel.Services.Chaos.GifCascadePayload { Ambient = true } } };
            var run = new AmbientBubble { Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec
                { VariantId = "htlink", Payload = new ConditioningControlPanel.Services.Chaos.GifCascadePayload() } };
            try
            {
                BubbleOverlay.Field.Bubbles.Clear();
                BubbleOverlay.Field.Bubbles.Add(trigger);
                BubbleOverlay.Field.Bubbles.Add(run);
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble());
                Assert.Equal(1, BubbleOverlay.ChaosCount);
                BubbleOverlay.EndChaos();
                Assert.Equal(2, BubbleOverlay.Field.Bubbles.Count);
                Assert.Contains(trigger, BubbleOverlay.Field.Bubbles);
                Assert.DoesNotContain(run, BubbleOverlay.Field.Bubbles);
                // Stopping the ambient game mid-run drops the plain and trigger bubbles, keeps the run's.
                Assert.False(trigger.IsRunBubble);
                Assert.True(run.IsRunBubble);
            }
            finally
            {
                BubbleOverlay.Field.Bubbles.Clear();
                BubbleOverlay.Field.Bubbles.AddRange(saved);
                BubbleOverlay.Field.HoldChaos = held;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Brain_drain_art_suppresses_tint_only_when_loaded()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var old = BubbleOverlay.BrainDrainImage;
            var oldImage = BubbleOverlay.Image;
            var oldRain = BubbleOverlay.GifRainImage;
            try
            {
                BubbleOverlay.LoadBubbleImage();
                var b = new AmbientBubble { Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec
                    { VariantId = ConditioningControlPanel.Services.Chaos.BrainDrainBubble.VariantId } };
                Assert.NotNull(BubbleOverlay.BrainDrainImage);
                Assert.Same(BubbleOverlay.BrainDrainImage, BubbleOverlay.SpriteFor(b));
                Assert.False(BubbleOverlay.DrawTint(b));
                BubbleOverlay.BrainDrainImage = null;
                Assert.True(BubbleOverlay.DrawTint(b));
            }
            finally { BubbleOverlay.BrainDrainImage = old; BubbleOverlay.Image = oldImage; BubbleOverlay.GifRainImage = oldRain; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Brain_drain_pop_fires_once_and_expiry_and_chaos_do_not_fire()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var field = BubbleOverlay.Field;
            var saved = field.Bubbles.ToArray();
            var old = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
            var calls = 0;
            var spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec {
                VariantId = ConditioningControlPanel.Services.Chaos.BrainDrainBubble.VariantId,
                Payload = new ConditioningControlPanel.Services.Chaos.BrainDrainMeltPayload { Ambient = true }, TreatLifeMs = 7000 };
            try
            {
                ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = p => { if (p is ConditioningControlPanel.Services.Chaos.BrainDrainMeltPayload) calls++; };
                field.Bubbles.Clear();
                var b = AmbientBubble.SpawnChaos(new System.Random(1), 0, 0, 0, 1920, 100000, 1, spec);
                field.Bubbles.Add(b);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                BubbleOverlay.EndChaos();
                Assert.Contains(b, field.Bubbles);
                BubbleOverlay.Pop(b);
                BubbleOverlay.Pop(b);
                Assert.Equal(1, calls);
                field.Bubbles.Clear();
                b = AmbientBubble.SpawnChaos(new System.Random(1), 0, 0, 0, 1920, 100000, 1, spec);
                field.Bubbles.Add(b);
                for (int i = 0; i < 219; i++) field.Step();
                Assert.True(b.Dissolving);
                Assert.Equal(1, calls);
            }
            finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = old; field.Bubbles.Clear(); field.Bubbles.AddRange(saved); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Gif_rain_duration_tracks_the_global_multiplier()
    {
        var old = ConditioningControlPanel.Services.Chaos.EffectPayload.GlobalDurationMult;
        try
        {
            ConditioningControlPanel.Services.Chaos.EffectPayload.GlobalDurationMult = 1.5;
            Assert.Equal(9.0, global::ConditioningControlPanel.Avalonia.App.GifRainDuration);
            ConditioningControlPanel.Services.Chaos.EffectPayload.GlobalDurationMult = 0.5;
            Assert.Equal(3.0, global::ConditioningControlPanel.Avalonia.App.GifRainDuration);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.GlobalDurationMult = old; }
    }

    /// <summary>Windows input region: the window takes clicks only while the cursor is inside a
    /// live bubble rect (Win32Overlay.SetInputRects toggles WS_EX_TRANSPARENT on this answer).</summary>
    [Fact]
    public void Win32_rect_hit_decision_takes_input_only_inside_live_rects()
    {
        var rects = new[] { new PixelRect(100, 100, 50, 50), new PixelRect(400, 0, 10, 10), new PixelRect(0, 0, 999, 999) };
        Assert.True(Win32Overlay.Hits(rects, 2, 120, 120));
        Assert.True(Win32Overlay.Hits(rects, 2, 405, 5));
        Assert.False(Win32Overlay.Hits(rects, 2, 150, 150));   // right/bottom edge is outside
        Assert.False(Win32Overlay.Hits(rects, 2, 300, 300));   // the third rect is past count
        Assert.False(Win32Overlay.Hits(rects, 0, 120, 120));   // empty field: fully click-through
    }
}
