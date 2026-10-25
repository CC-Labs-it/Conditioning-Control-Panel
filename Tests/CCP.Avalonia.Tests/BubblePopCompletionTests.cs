using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Fonts.Inter;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubblePopCompletionTests
{
    [Fact]
    public async Task Repeated_pop_does_not_repeat_the_reward_signal()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current;
            var (day, paid) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);
            var count = 0;
            void Signal() => count++;
            BubbleOverlay.Popped += Signal;
            try
            {
                var bubble = new global::ConditioningControlPanel.Services.AmbientBubble();
                BubbleOverlay.Pop(bubble);
                BubbleOverlay.Pop(bubble);
                Assert.Equal(1, count);
            }
            finally
            {
                BubbleOverlay.Popped -= Signal;
                s.AmbientBubbleXpDayKey = day; s.AmbientBubbleXpPaidToday = paid;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Panic_closes_the_bubble_milestone_surface()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var popup = new global::ConditioningControlPanel.Avalonia.Views.Windows.AchievementPopup("100", "milestone", "bubble_pop.png");
            popup.Show();
            try
            {
                var stop = System.Linq.Enumerable.Single(global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All, s => s.Id == "achievement-popups");
                stop.Stop(null);
                Assert.False(popup.IsVisible);
                stop.Stop(null);
            }
            finally { popup.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Mod_pop_chime_opt_out_and_missing_chime_follow_Wpf_fallback()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var oldResolve = CoreModArt.AudioOverridePathProvider;
            var oldPlay = CoreAudio.PlayOneShotProvider;
            var s = CoreSettings.Current;
            var (master, volume, suppress) = (s.MasterVolume, s.BubblesVolume, s.SuppressPerkNotifications);
            var dir = Directory.CreateTempSubdirectory("ccp-pop-audio-");
            var pop = Path.Combine(dir.FullName, "pop.wav"); File.WriteAllBytes(pop, new byte[1]);
            var chime = Path.Combine(dir.FullName, "chime.wav"); File.WriteAllBytes(chime, new byte[1]);
            string? heard = null; float level = 0;
            try
            {
                s.MasterVolume = 50; s.BubblesVolume = 80; s.SuppressPerkNotifications = false;
                CoreModArt.AudioOverridePathProvider = name => name.StartsWith("bubbles/") ? pop : chime;
                CoreAudio.PlayOneShotProvider = (path, vol, _, _, _) => { heard = path; level = vol; };
                BubbleOverlay.PlayPopSound(false);
                Assert.Equal(pop, heard); Assert.Equal(Math.Pow(0.4, 1.5), level, 6);
                BubbleOverlay.PlayPopSound(true);
                Assert.Equal(chime, heard); Assert.Equal(Math.Pow(0.4, 1.5) * 0.35, level, 6);
                s.SuppressPerkNotifications = true;
                BubbleOverlay.PlayPopSound(true); Assert.Equal(pop, heard);
                s.SuppressPerkNotifications = false;
                File.Delete(chime);
                BubbleOverlay.PlayPopSound(true); Assert.Equal(pop, heard);
            }
            finally
            {
                CoreModArt.AudioOverridePathProvider = oldResolve; CoreAudio.PlayOneShotProvider = oldPlay;
                s.MasterVolume = master; s.BubblesVolume = volume; s.SuppressPerkNotifications = suppress;
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Owned_motion_picker_round_trips_and_unowned_style_displays_float_up()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var oldGrant = PrizeOwnership.IsGranted;
            var s = CoreSettings.Current; var oldStyle = s.BubbleMotionStyle;
            try
            {
                PrizeOwnership.IsGranted = _ => true;
                s.BubbleMotionStyle = BubbleMotionStyle.SpiralIn;
                var card = new BubblePopFeatureControl();
                var combo = card.FindControl<ComboBox>("CmbMotion")!;
                Assert.Equal(4, combo.Items.Count);
                Assert.Equal(BubbleMotionStyle.SpiralIn, ((ComboBoxItem)combo.SelectedItem!).Tag);
                combo.SelectedIndex = 1;
                Assert.Equal(BubbleMotionStyle.Rain, s.BubbleMotionStyle);
                PrizeOwnership.IsGranted = _ => false;
                card = new BubblePopFeatureControl();
                combo = card.FindControl<ComboBox>("CmbMotion")!;
                Assert.Single(combo.Items);
                Assert.Equal(BubbleMotionStyle.FloatUp, ((ComboBoxItem)combo.SelectedItem!).Tag);
                Assert.Equal(BubbleMotionStyle.Rain, s.BubbleMotionStyle);
            }
            finally { PrizeOwnership.IsGranted = oldGrant; s.BubbleMotionStyle = oldStyle; }
            return Task.CompletedTask;
        });
    }
}
