using System;
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
    [Theory]
    [InlineData(1.0, 0, false)]
    [InlineData(1.5, -1920, false)]
    [InlineData(1.0, 0, true)]
    public async Task Pop_draws_particles_above_the_sprite_with_screen_scaling(double scale, int left, bool gold)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var old = (s.FlashGlowEnabled, s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);
            var added = !s.UnlockedSkills.Contains("sparkle_boost_1");
            var lucky = s.UnlockedSkills.Remove("lucky_bubbles");
            var w = new BubbleOverlayWindow(0, new PixelRect(left, 0, 600, 600), scale);
            try
            {
                if (added) s.UnlockedSkills.Add("sparkle_boost_1");
                s.FlashGlowEnabled = true;
                s.AmbientBubbleXpDayKey = "";
                var b = new AmbientBubble { X = left / scale + 100, Y = 100, Size = 100, Screen = 0 };
                BubbleOverlay.Field.Bubbles.Clear();
                BubbleOverlay.Field.Bubbles.Add(b);
                BubbleOverlay.Pop(b); // exercise the real head path, not a render-only fixture
                Assert.Equal(8, b.Sparkles.Length);
                // A particle outside the sprite: it must not inherit the sprite's scale/rotation.
                b.Angle = 90; b.Scale = 1.6; b.Fade = 0.5; b.Lucky = gold;
                for (var i = 0; i < b.Sparkles.Length; i++)
                    b.Sparkles[i] = new AmbientBubble.Sparkle { X = 150, Y = 50, Size = 6, Alpha = i == 0 ? 0.5 : 0 };
                var layer = (Control)w.Content!;
                layer.Measure(new Size(600, 600));
                layer.Arrange(new Rect(0, 0, 600, 600));
                using var image = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(600, 600));
                image.Render(layer);
                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
                try
                {
                    image.CopyPixels(new PixelRect((int)(250 * scale), (int)(150 * scale), 1, 1), buf, 4, 4);
                    var pixel = unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf));
                    Assert.InRange(pixel >> 24, 63u, 64u);
                    var green = (pixel >> 8) & 255;
                    var blue = pixel & 255;
                    Assert.True(gold ? green > blue : blue > green, $"Wrong particle colour: {pixel:X8}");
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
                b.Popping = true;
                w.Sync();
                Assert.Equal(0, w.InputRectCount); // sparkles never intercept desktop input
            }
            finally
            {
                BubbleOverlay.Field.Bubbles.Clear(); w.Close();
                if (added) s.UnlockedSkills.Remove("sparkle_boost_1");
                if (lucky) s.UnlockedSkills.Add("lucky_bubbles");
                (s.FlashGlowEnabled, s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday) = old;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Ambient_pop_credits_achievements_once_after_the_xp_cap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var dir = System.IO.Directory.CreateTempSubdirectory("ccp-bubble-head-credit-");
            var property = typeof(global::ConditioningControlPanel.Avalonia.App).GetProperty("Achievements",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var previous = property.GetValue(null);
            var s = CoreSettings.Current;
            var (day, paid) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);
            try
            {
                var engine = new AchievementEngine(new AchievementStore(System.IO.Path.Combine(dir.FullName, "achievements.json")));
                property.SetValue(null, engine);
                s.AmbientBubbleXpDayKey = "";
                AmbientBubbleXp.Take(s, AmbientBubbleXp.DailyXpCap);
                var bubble = new AmbientBubble();
                BubbleOverlay.Pop(bubble);
                BubbleOverlay.Pop(bubble);
                Assert.Equal(1, engine.Progress.TotalBubblesPopped);
                Assert.Equal(AmbientBubbleXp.DailyXpCap, AmbientBubbleXp.PaidToday(s));
            }
            finally
            {
                property.SetValue(null, previous);
                s.AmbientBubbleXpDayKey = day; s.AmbientBubbleXpPaidToday = paid;
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Frozen_chaos_bubble_draws_an_aura_that_clears_on_thaw()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var field = BubbleOverlay.Field;
            var window = new BubbleOverlayWindow(0, new PixelRect(0, 0, 240, 240), 1) { Width = 240, Height = 240 };
            try
            {
                field.Bubbles.Clear();
                field.Bubbles.Add(new AmbientBubble { X = 70, Y = 70, Size = 100,
                    Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec { Tint = 0xFF80A0FF } });
                window.Show();
                window.UpdateLayout();
                byte[] Frame()
                {
                    using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(240, 240));
                    bitmap.Render((Control)window.Content!);
                    var pixels = new byte[240 * 240 * 4];
                    var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(pixels.Length);
                    try
                    {
                        bitmap.CopyPixels(new PixelRect(0, 0, 240, 240), ptr, pixels.Length, 240 * 4);
                        System.Runtime.InteropServices.Marshal.Copy(ptr, pixels, 0, pixels.Length);
                    }
                    finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr); }
                    return pixels;
                }
                field.FreezeChaos = false;
                var normal = Frame();
                field.FreezeChaos = true;
                var frozen = Frame();
                Assert.False(System.Linq.Enumerable.SequenceEqual(normal, frozen));
                field.FreezeChaos = false;
                Assert.Equal(normal, Frame());
            }
            finally { field.FreezeChaos = false; field.ClearFreezeVisuals(); field.Bubbles.Clear(); window.Close(); }
            return Task.CompletedTask;
        });
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
    public void One_shots_obey_cap_and_retire_without_starting_ambient_schedule() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var host = new Window();
        var oldChecks = BubbleOverlay.SkipPlatformChecksForTests;
        var oldShared = CoreSettings.Current.BubbleSharedHost;
        try
        {
            BubbleOverlay.SkipPlatformChecksForTests = true;
            CoreSettings.Current.BubbleSharedHost = false; // legacy cap of three
            host.Show();
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.NotifyDisplayChange("one-shot test");
            BubbleOverlay.SpawnOnce(host);
            Assert.Empty(BubbleOverlay.Field.Bubbles);
            Assert.Equal(0, BubbleOverlay.WindowCount);
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.ResetForTests();
            for (var i = 0; i < 5; i++) BubbleOverlay.SpawnOnce(host);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count);
            Assert.False(BubbleOverlay.IsRunning);
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.OnFrame(System.TimeSpan.FromSeconds(1));
            BubbleOverlay.OnFrame(System.TimeSpan.FromSeconds(2));
            Assert.Empty(BubbleOverlay.Field.Bubbles);
            Assert.False(BubbleOverlay.IsRunning);
            Assert.Equal(0, BubbleOverlay.WindowCount);
            // The retired windows must be reopened for the next reward.
            BubbleOverlay.SpawnOnce(host);
            Assert.Single(BubbleOverlay.Field.Bubbles);
            BubbleOverlay.Start(host);
            Assert.True(BubbleOverlay.IsRunning); // ambient takes over existing one-shot windows
            Assert.Equal(2, BubbleOverlay.Field.Bubbles.Count); // immediate first ambient spawn
        }
        finally
        {
            BubbleOverlay.Stop();
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.ResetForTests();
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks;
            CoreSettings.Current.BubbleSharedHost = oldShared;
            host.Close();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void End_chaos_retires_parked_one_shot_windows_unless_ambient_owns_them(bool ambient) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var host = new Window();
        var oldChecks = BubbleOverlay.SkipPlatformChecksForTests;
        try
        {
            BubbleOverlay.SkipPlatformChecksForTests = true;
            host.Show();
            BubbleOverlay.SpawnOnce(host);
            Assert.Single(BubbleOverlay.Field.Bubbles);
            if (ambient) BubbleOverlay.Start(host);
            var windows = BubbleOverlay.WindowCount;
            Assert.True(windows > 0);
            BubbleOverlay.BeginChaos(host, _ => { });
            // Model expiry, then run the last frame while Chaos still owns the field.
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.OnFrame(System.TimeSpan.FromSeconds(100));
            Assert.Empty(BubbleOverlay.Field.Bubbles);
            Assert.Equal(windows, BubbleOverlay.WindowCount);
            BubbleOverlay.EndChaos();
            // No later frame is available to do this cleanup.
            Assert.Equal(ambient ? windows : 0, BubbleOverlay.WindowCount);
            Assert.Equal(ambient, BubbleOverlay.IsRunning);
        }
        finally
        {
            BubbleOverlay.EndChaos();
            BubbleOverlay.Stop();
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks;
            host.Close();
        }
    });

    /// <summary>WPF BubbleService.cs:221 LoadBubbleImage: each Start re-resolves bubble.png through the
    /// active mod, so after a mod switch the next start draws the new mod's sprite, not the cached one.</summary>
    [Fact]
    public async Task Each_start_reads_the_active_mods_bubble_sprite()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
                Assert.Contains("5/300", line.Text);   // preserve the original live budget assertion
                var pops = 0;
                void Count() => pops++;
                BubbleOverlay.Popped += Count;
                try
                {
                    s.AmbientBubbleXpPaidToday = AmbientBubbleXp.DailyXpCap;
                    var dry = new AmbientBubble();
                    BubbleOverlay.Pop(dry);
                    BubbleOverlay.Pop(dry);
                    Assert.Equal(1, pops);
                    Assert.Equal(AmbientBubbleXp.DailyXpCap, AmbientBubbleXp.PaidToday(s));
                }
                finally { BubbleOverlay.Popped -= Count; }
                Assert.Contains("300/300", line.Text);   // the card's N/300 line follows the pop
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
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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

    /// <summary>WPF BubbleService.cs:4967: with no per-variant sprite the freeze pickup wears its
    /// label glyph, so it reads as a freeze before it is caught. Same bubble, label vs none.</summary>
    [Fact]
    public async Task Freeze_pickup_draws_its_label_glyph()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var saved = BubbleOverlay.Field.Bubbles.ToArray();
            var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1280, 720), 1) { Width = 1280, Height = 720 };
            ConditioningControlPanel.Services.Chaos.EffectBubbleSpec Spec(string label) => new()
                { VariantId = "bambifreeze", IsFreeze = true, SizePx = 200, Tint = 0xFF8AE6FFu, Label = label };
            var b = new AmbientBubble { Screen = 0, X = 400, Y = 200, Size = 200, Clickable = true, Spec = Spec("\u2744") };
            int stride = 0;
            byte[] Frame()
            {
                (w.Content as Control)!.InvalidateVisual();
                using var bmp = w.CaptureRenderedFrame()!;
                using var fb = bmp.Lock();
                stride = fb.RowBytes;
                var px = new byte[fb.RowBytes * fb.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(fb.Address, px, 0, px.Length);
                return px;
            }
            try
            {
                BubbleOverlay.Field.Bubbles.Clear();
                BubbleOverlay.Field.Bubbles.Add(b);
                w.Show();
                var labelled = Frame();
                b.Spec = Spec("");
                var plain = Frame();
                int inside = 0, outside = 0;
                for (int i = 0; i < labelled.Length; i += 4)
                {
                    if (labelled[i] == plain[i] && labelled[i + 1] == plain[i + 1] && labelled[i + 2] == plain[i + 2]) continue;
                    int x = i % stride / 4, y = i / stride;
                    if (Math.Abs(x - 500) < 50 && Math.Abs(y - 300) < 50) inside++; else outside++;
                }
                Assert.True(inside > 100, $"label pixels {inside}");
                Assert.Equal(0, outside);
            }
            finally
            {
                w.Close();
                BubbleOverlay.Field.Bubbles.Clear();
                BubbleOverlay.Field.Bubbles.AddRange(saved);
            }
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
