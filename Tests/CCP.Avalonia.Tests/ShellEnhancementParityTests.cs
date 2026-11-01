using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ShellEnhancementParityTests
{
    private sealed class FixedRandom : Random { public override double NextDouble() => 0; public override int Next(int max) => 0; public override int Next(int min, int max) => min; }
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // WPF BubbleService.cs:1082-1111,2191-2227: mute falls through to pop without changing payout.
    [Fact]
    public Task Lucky_chime_opt_out_and_missing_chime_fall_back_to_normal_pop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var oldSkills = s.UnlockedSkills.ToList();
        var (rng, audio, resolve, xp, quiet, day, budget, master, volume) = (BubbleOverlay.Field.Random, CoreAudio.PlayOneShotProvider,
            CoreModArt.AudioOverridePathProvider, CoreProgression.AddXPProvider, s.SuppressPerkNotifications,
            s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday, s.MasterVolume, s.BubblesVolume);
        var dir = Directory.CreateTempSubdirectory("ccp-pop-");
        var chime = Path.Combine(dir.FullName, "chime.mp3");
        var pop = Path.Combine(dir.FullName, "pop.mp3");
        File.WriteAllText(chime, ""); File.WriteAllText(pop, "");
        string? played = null; float playedVolume = 0; double paid = 0; bool missing = false; int plays = 0;
        try
        {
            s.UnlockedSkills = ["lucky_bubbles"];
            s.AmbientBubbleXpDayKey = DateTime.Now.ToString("yyyy-MM-dd"); s.AmbientBubbleXpPaidToday = 0;
            s.MasterVolume = s.BubblesVolume = 100;
            BubbleOverlay.Field.Random = new FixedRandom();
            CoreModArt.AudioOverridePathProvider = name => name.StartsWith("chime") ? (missing ? Path.Combine(dir.FullName, "missing") : chime) : pop;
            CoreAudio.PlayOneShotProvider = (path, v, _, _, _) => { played = path; playedVolume = v; plays++; };
            CoreProgression.AddXPProvider = (amount, _) => paid += amount;
            s.SuppressPerkNotifications = false;
            var first = new AmbientBubble(); BubbleOverlay.Pop(first);
            Assert.True(first.Lucky); Assert.Equal(chime, played); Assert.Equal(0.35f, playedVolume, 3);
            s.SuppressPerkNotifications = true;
            var muted = new AmbientBubble(); BubbleOverlay.Pop(muted);
            Assert.True(muted.Lucky); Assert.Equal(pop, played); Assert.Equal(1f, playedVolume);
            Assert.Equal(200, paid);
            s.SuppressPerkNotifications = false; missing = true;
            BubbleOverlay.Pop(new AmbientBubble());
            Assert.Equal(pop, played); Assert.Equal(1f, playedVolume);
            BubbleOverlay.Pop(first); Assert.Equal(300, paid); Assert.Equal(3, plays); // WPF Pop guard: :4645-4647
        }
        finally
        {
            LuckyToast.Close(); BubbleOverlay.Field.Random = rng; CoreAudio.PlayOneShotProvider = audio;
            CoreModArt.AudioOverridePathProvider = resolve; CoreProgression.AddXPProvider = xp;
            s.UnlockedSkills = oldSkills; s.SuppressPerkNotifications = quiet; s.AmbientBubbleXpDayKey = day;
            s.AmbientBubbleXpPaidToday = budget; s.MasterVolume = master; s.BubblesVolume = volume;
            dir.Delete(true);
        }
        return Task.CompletedTask;
    });

    // WPF BubbleService.cs:4401-4470: the paid pop paints a gold glow and particles.
    [Fact]
    public Task Lucky_glow_and_sparkles_are_drawn_and_performance_tier_omits_glow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (skills, perf, auto, rng, audio, xp, quiet, day, budget, image) = (s.UnlockedSkills.ToList(), s.PerformanceMode,
            s.AutoPerformanceMode, BubbleOverlay.Field.Random, CoreAudio.PlayOneShotProvider, CoreProgression.AddXPProvider,
            s.SuppressPerkNotifications, s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday, BubbleOverlay.Image);
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 240, 240), 1) { Width = 240, Height = 240 };
        try
        {
            s.UnlockedSkills = ["lucky_bubbles"]; s.PerformanceMode = false; s.AutoPerformanceMode = false;
            s.SuppressPerkNotifications = true; s.AmbientBubbleXpDayKey = DateTime.Now.ToString("yyyy-MM-dd"); s.AmbientBubbleXpPaidToday = 0;
            BubbleOverlay.Field.Random = new FixedRandom(); CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
            CoreProgression.AddXPProvider = (_, _) => { }; BubbleOverlay.Image = null;
            var b = new AmbientBubble { X = 90, Y = 90, Size = 60 };
            BubbleOverlay.Field.Bubbles.Add(b); BubbleOverlay.Pop(b);
            Assert.Equal(24, b.LuckyGlowBlur); Assert.Equal(16, b.Sparkles.Length);
            w.Show(); Dispatcher.UIThread.RunJobs();
            byte[] Render()
            {
                using var frame = new RenderTargetBitmap(new PixelSize(240, 240)); frame.Render((Control)w.Content!);
                var bytes = new byte[240 * 240 * 4]; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try { frame.CopyPixels(new PixelRect(0, 0, 240, 240), pinned.AddrOfPinnedObject(), bytes.Length, 240 * 4); }
                finally { pinned.Free(); }
                return bytes;
            }
            // Separate the burst from the sprite so the rendered pixels prove both paths.
            var sp = b.Sparkles[0]; sp.X = 50; sp.Y = 0; sp.Size = 8; b.Sparkles = [sp];
            var both = Render();
            b.Sparkles = []; var glow = Render();
            Assert.False(both.SequenceEqual(glow));
            b.LuckyGlowBlur = 0; var plain = Render();
            Assert.False(glow.SequenceEqual(plain));
            s.PerformanceMode = true;
            var perfBubble = new AmbientBubble(); BubbleOverlay.Pop(perfBubble);
            Assert.True(perfBubble.Lucky); Assert.Equal(0, perfBubble.LuckyGlowBlur); Assert.Equal(16, perfBubble.Sparkles.Length);
        }
        finally
        {
            w.Close(); BubbleOverlay.Field.Bubbles.Clear(); LuckyToast.Close(); BubbleOverlay.Image = image;
            s.UnlockedSkills = skills; s.PerformanceMode = perf; s.AutoPerformanceMode = auto; s.SuppressPerkNotifications = quiet;
            s.AmbientBubbleXpDayKey = day; s.AmbientBubbleXpPaidToday = budget;
            BubbleOverlay.Field.Random = rng; CoreAudio.PlayOneShotProvider = audio; CoreProgression.AddXPProvider = xp;
        }
        return Task.CompletedTask;
    });
}
