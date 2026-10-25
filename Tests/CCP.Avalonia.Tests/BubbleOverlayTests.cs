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
    [Fact]
    public void Chaos_teardown_preserves_ambient_effect_bubbles()
    {
        var ambient = new AmbientBubble { AmbientTrigger = true, Spec = AmbientBubbleTriggers.Build("pink", "Mixed") };
        var chaos = new AmbientBubble { Spec = AmbientBubbleTriggers.Build("pink", "Mixed") };
        try
        {
            BubbleOverlay.Field.Bubbles.Add(ambient); BubbleOverlay.Field.Bubbles.Add(chaos);
            Assert.Equal(1, BubbleOverlay.ChaosCount);
            BubbleOverlay.EndChaos();
            Assert.Contains(ambient, BubbleOverlay.Field.Bubbles);
            Assert.DoesNotContain(chaos, BubbleOverlay.Field.Bubbles);
        }
        finally { BubbleOverlay.Field.Bubbles.Clear(); }
    }

    [Fact]
    public void Pop_sound_resolves_mod_override_and_suppressed_lucky_falls_back()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-bubble-sound-");
        var path = System.IO.Path.Combine(dir.FullName, "cue.wav");
        System.IO.File.WriteAllBytes(path, new byte[0]);
        var previous = CoreModArt.AudioOverridePathProvider;
        var requests = new System.Collections.Generic.List<string>();
        try
        {
            CoreModArt.AudioOverridePathProvider = cue => { requests.Add(cue); return path; };
            Assert.Equal((path, true), BubbleOverlay.ResolvePopSound(true, false, new System.Random(1)));
            Assert.StartsWith("chime", requests[0]);
            requests.Clear();
            Assert.Equal((path, false), BubbleOverlay.ResolvePopSound(true, true, new System.Random(1)));
            Assert.StartsWith("bubbles/Pop", requests[0]);
            requests.Clear();
            CoreModArt.AudioOverridePathProvider = cue => { requests.Add(cue); return cue.StartsWith("chime") ? path + ".missing" : path; };
            Assert.Equal((path, false), BubbleOverlay.ResolvePopSound(true, false, new System.Random(1)));
            Assert.Equal(2, requests.Count);
        }
        finally { CoreModArt.AudioOverridePathProvider = previous; dir.Delete(true); }
    }

    [Fact]
    public async Task Trigger_pop_fires_payload_and_pays_once_even_when_reentered()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var settings = CoreSettings.Current;
            var (day, paid) = (settings.AmbientBubbleXpDayKey, settings.AmbientBubbleXpPaidToday);
            var firer = global::ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
            var fires = 0;
            try
            {
                settings.AmbientBubbleXpDayKey = "";
                global::ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = _ => fires++;
                var b = new AmbientBubble { AmbientTrigger = true, Spec = AmbientBubbleTriggers.Build("pink", "Mixed") };
                Assert.NotNull(b.Spec);
                BubbleOverlay.Pop(b);
                BubbleOverlay.Pop(b);
                Assert.Equal(1, fires);
                Assert.Equal(5, AmbientBubbleXp.PaidToday(settings));
            }
            finally
            {
                global::ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = firer;
                settings.AmbientBubbleXpDayKey = day; settings.AmbientBubbleXpPaidToday = paid;
            }
            return Task.CompletedTask;
        });
    }

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

    [Theory]
    [InlineData(1.0, 0, 0, 20)]
    [InlineData(1.5, -1920, -240, 20)]
    [InlineData(1.25, -1920, 0, -20)]
    [InlineData(2.0, 0, 0, 260)]
    public async Task Native_region_matches_the_hit_disc_including_transparent_corners(double scale, int left, int top, int offset)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var bounds = new PixelRect(left, top, 300, 300);
            var w = new BubbleOverlayWindow(0, bounds, scale);
            var b = new AmbientBubble { Screen = 0, X = (left + offset) / scale, Y = (top + offset) / scale, Size = 100 / scale, Clickable = true };
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Bubbles.Add(b);
            try
            {
                w.Sync();
                // Every native pixel must agree with Core's WPF-style disc decision, including
                // the exact radius, negative screen origin and mixed DPI conversion.
                for (var y = 0; y < bounds.Height; y++)
                    for (var x = 0; x < bounds.Width; x++)
                        Assert.Equal(b.Contains((left + x) / scale, (top + y) / scale),
                            Win32Overlay.Hits(w.Rects, w.InputRectCount, x, y));
                b.Popping = true;
                w.Sync();
                Assert.False(Win32Overlay.Hits(w.Rects, w.InputRectCount, 70, 70));
            }
            finally { BubbleOverlay.Field.Bubbles.Clear(); w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Dense_field_keeps_input_for_bubbles_after_the_first_64()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1600, 100), 1);
            BubbleOverlay.Field.Bubbles.Clear();
            for (var i = 0; i < 70; i++)
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { Screen = 0, X = i * 20, Y = 20, Size = 10, Clickable = true });
            try
            {
                w.Sync();
                for (var i = 0; i < 70; i++)
                    Assert.True(Win32Overlay.Hits(w.Rects, w.InputRectCount, i * 20 + 5, 25));
                // A different screen, a non-clickable bubble and a popping bubble must all
                // leave the native input region when the field shrinks.
                BubbleOverlay.Field.Bubbles[0].Screen = 1;
                BubbleOverlay.Field.Bubbles[1].Clickable = false;
                BubbleOverlay.Field.Bubbles[2].Popping = true;
                BubbleOverlay.Field.Bubbles.RemoveRange(3, 67);
                w.Sync();
                Assert.False(Win32Overlay.Hits(w.Rects, w.InputRectCount, 5, 25));
                Assert.False(Win32Overlay.Hits(w.Rects, w.InputRectCount, 25, 25));
                Assert.False(Win32Overlay.Hits(w.Rects, w.InputRectCount, 45, 25));
                Assert.False(Win32Overlay.Hits(w.Rects, w.InputRectCount, 69 * 20 + 5, 25));
            }
            finally { BubbleOverlay.Field.Bubbles.Clear(); w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Dense_chaos_field_region_stays_bounded_and_cheap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            // 120 large bubbles at 150 %: r = 112 px, so ~225 pixel rows per bubble before coalescing.
            var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 2560, 1440), 1.5);
            BubbleOverlay.Field.Bubbles.Clear();
            for (var i = 0; i < 120; i++)
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { Screen = 0, X = (i % 12) * 140 + 0.37 * i, Y = (i / 12) * 90 + 0.21 * i, Size = 150, Clickable = true });
            try
            {
                w.Sync();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (var k = 0; k < 20; k++) w.Sync();
                var ms = sw.Elapsed.TotalMilliseconds / 20;
                var report = $"120 bubbles @150%: {w.InputRectCount} rects, {ms:F3} ms/sync";
                // Identical adjacent rows coalesce, so the region stays well under one rect per row.
                Assert.True(w.InputRectCount is >= 120 and <= 120 * 160, report);
                Assert.True(ms < 30, report + " (budget 30 ms = one step)");
            }
            finally { BubbleOverlay.Field.Bubbles.Clear(); w.Close(); }
            return Task.CompletedTask;
        });
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
