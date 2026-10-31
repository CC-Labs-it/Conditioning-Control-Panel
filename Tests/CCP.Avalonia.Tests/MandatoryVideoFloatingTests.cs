using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF bd659f9f3: the Video card's "Shows as" switch, and a floating mandatory video is ONE
/// small, non-activating window instead of full screen, with no attention checks; strict stays full screen.</summary>
public sealed class MandatoryVideoFloatingTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task The_card_offers_full_screen_and_floating_and_saves_the_pick()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var saved = s.MandatoryVideoFloating;
            s.MandatoryVideoFloating = false;
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            host.Show();
            try
            {
                var cmb = card.FindControl<ComboBox>("CmbAppearance")!;
                var note = card.FindControl<TextBlock>("TxtAppearanceNote")!;
                var items = cmb.Items.OfType<ComboBoxItem>().ToList();
                Assert.Equal(new object?[] { Loc.Get("video_appearance_fullscreen"), Loc.Get("video_appearance_floating") }, items.Select(i => i.Content));
                Assert.Same(items[0], cmb.SelectedItem);
                Assert.False(note.IsVisible);

                cmb.SelectedItem = items[1];
                Dispatcher.UIThread.RunJobs();
                Assert.True(s.MandatoryVideoFloating);
                Assert.True(note.IsVisible, "the strict note shows while floating is picked");

                cmb.SelectedItem = cmb.Items.OfType<ComboBoxItem>().First();
                Dispatcher.UIThread.RunJobs();
                Assert.False(s.MandatoryVideoFloating);
            }
            finally
            {
                host.Close();
                s.MandatoryVideoFloating = saved;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void A_Lockdown_keeps_the_video_full_screen()
    {
        var s = CoreSettings.Current;
        var (saved, previous) = (s.MandatoryVideoFloating, ConditioningControlPanel.Services.LockdownService.Current);
        try
        {
            s.MandatoryVideoFloating = true;
            Assert.True(MandatoryVideoOverlay.Floats(strict: false));
            var ld = ConditioningControlPanel.Services.LockdownService.Current = new ConditioningControlPanel.Services.LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.False(MandatoryVideoOverlay.Floats(strict: false));
        }
        finally
        {
            ConditioningControlPanel.Services.LockdownService.Current?.Deactivate();
            ConditioningControlPanel.Services.LockdownService.Current = previous;
            s.MandatoryVideoFloating = saved;
        }
    }

    [Fact]
    public async Task A_floating_video_is_one_small_unfocused_window_and_strict_stays_full_screen()
    {
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
        var s = CoreSettings.Current;
        var saved = (s.MandatoryVideoFloating, s.AttentionChecksEnabled, s.AudioDuckingEnabled, s.MandatoryVideoFloatingSizePercent);
        var o = MandatoryVideoOverlay.Instance;
        var clip = Path.Combine(Path.GetTempPath(), "ccp-missing-" + Guid.NewGuid().ToString("N") + ".mp4");
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            try
            {
                (s.MandatoryVideoFloating, s.AttentionChecksEnabled, s.AudioDuckingEnabled, s.MandatoryVideoFloatingSizePercent) = (true, true, false, 33);

                o.Show(clip, strict: false);
                var w = Assert.Single(o.Windows);
                Assert.True(o.Floating);
                Assert.False(o.AttentionArmed, "a floating video has no attention checks");
                Assert.True(w.Topmost);
                Assert.False(w.IsVisible, "no black window before the first presented frame");
                Assert.NotEqual(WindowState.FullScreen, w.WindowState);
                Assert.False(w.ShowActivated, "a floating window never takes focus");
                var scr = w.Screens.Primary ?? w.Screens.All[0];
                var areaW = (int)(scr.WorkingArea.Width / scr.Scaling);
                var expected = MandatoryVideoPresentation.FloatingRect(areaW, (int)(scr.WorkingArea.Height / scr.Scaling), new Random(1), 33);
                Assert.Equal((double)expected.W, w.Width);
                Assert.Equal((double)expected.H, w.Height);
                Assert.True(w.Width < areaW, "smaller than the screen");
                o.CloseAll();
                Assert.Empty(o.Windows);

                // Strict always covers the screen, and keeps its attention checks.
                o.Show(clip, strict: true);
                w = Assert.Single(o.Windows);
                Assert.False(o.Floating);
                Assert.True(o.AttentionArmed);
                Assert.Equal(WindowState.FullScreen, w.WindowState);
                o.CloseAll();

                // Floating off: full screen as always.
                s.MandatoryVideoFloating = false;
                o.Show(clip, strict: false);
                Assert.False(o.Floating);
                Assert.Equal(WindowState.FullScreen, Assert.Single(o.Windows).WindowState);
            }
            finally
            {
                o.CloseAll();
                (s.MandatoryVideoFloating, s.AttentionChecksEnabled, s.AudioDuckingEnabled, s.MandatoryVideoFloatingSizePercent) = saved;
            }
            return Task.CompletedTask;
        });
    }
}
