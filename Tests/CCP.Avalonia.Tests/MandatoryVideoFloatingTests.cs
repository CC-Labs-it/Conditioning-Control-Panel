using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
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
        var saved = (s.MandatoryVideoFloating, s.AttentionChecksEnabled, s.AudioDuckingEnabled, s.MandatoryVideoFloatingSizePercent,
            s.PanicOverridesAll, s.MasterVolume, s.VideoVolume);
        var o = MandatoryVideoOverlay.Instance;
        var previous = (o.Scheduler, o.Time);
        var graceStamp = typeof(MandatoryVideoOverlay).GetField("_lastGraceUtc",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var previousStamp = graceStamp.GetValue(o);
        var clock = new HeldPreroll();
        var graceScheduler = new ConditioningControlPanel.Services.MandatoryVideoScheduler(o, clock);
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
                Assert.Contains(WindowTransparencyLevel.Transparent, w.TransparencyLevelHint);
                Assert.InRange(w.Opacity, double.Epsilon, 0.001);
                Assert.False(w.IsHitTestVisible);
                Assert.Equal(0, o.PlayerVolume);
                typeof(MandatoryVideoOverlay).GetMethod("CheckFloatingReveal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(o, new object[] { true });
                Assert.Equal(1, w.Opacity);
                Assert.True(w.IsHitTestVisible);
                Assert.False(o.AttentionArmed, "a floating video has no attention checks");
                Assert.True(w.Topmost && w.IsVisible);
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

                // A panic during the veil must leave a visible, usable Resume card (WPF ShowGraceOverlays).
                (o.Scheduler, o.Time) = (graceScheduler, clock);
                (s.PanicOverridesAll, s.MasterVolume, s.VideoVolume) = (false, 80, 50);
                s.MandatoryVideoFloatingSizePercent = MandatoryVideoPresentation.MaxSizePercent;
                Assert.True(graceScheduler.Trigger(strictOverride: false, path: clip));
                o.Show(clip, strict: false);
                var paused = Assert.Single(o.Surfaces);
                Assert.Equal(-1, o.FirstFrameMs);
                Assert.InRange(paused.Window.Opacity, double.Epsilon, 0.001);
                Assert.False(paused.Window.IsHitTestVisible);
                o.ReadyAgeOverrideMs = 0;
                Assert.True(o.TryGracePause(fromPanicKey: true));
                Assert.True(o.GracePaused && paused.Grace.IsVisible);
                var card = AssertCentredCard(paused);
                var resume = Assert.Single(paused.Grace.GetVisualDescendants().OfType<Button>());
                resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(o.GracePaused || paused.Grace.IsVisible);
                Assert.False(card.IsVisible);
                Assert.InRange(paused.Window.Opacity, double.Epsilon, 0.001);
                Assert.False(paused.Window.IsHitTestVisible);
                o.UpdateVolume();
                Assert.Equal(0, o.PlayerVolume); // showing the pause card did not admit the video
                var reveal = typeof(MandatoryVideoOverlay).GetMethod("CheckFloatingReveal",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                reveal.Invoke(o, new object[] { false });
                Assert.InRange(paused.Window.Opacity, double.Epsilon, 0.001);
                reveal.Invoke(o, new object[] { true });
                Assert.Equal(1, paused.Window.Opacity);
                Assert.True(paused.Window.IsHitTestVisible);
                Assert.Equal(40, o.PlayerVolume); // only frame admission restores playback volume
                graceScheduler.ForceCleanup();

                // Auto-resume must also preserve a pending veil and the no-play timeout.
                s.MandatoryVideoFloatingSizePercent = MandatoryVideoPresentation.MinSizePercent;
                clock.Advance(TimeSpan.FromSeconds(1));
                Assert.True(graceScheduler.Trigger(strictOverride: false, path: clip));
                o.Show(clip, strict: false);
                var stalled = Assert.Single(o.Surfaces);
                Assert.True(stalled.Window.Height < 200, "the small floating window cannot hold the card");
                Assert.Null(o.ReadyAgeOverrideMs); // a fresh clip gets a fresh budget
                o.ReadyAgeOverrideMs = FloatingRevealGate.ReadyBudgetMs;
                Assert.True(o.TryGracePause(fromPanicKey: true));
                card = AssertCentredCard(stalled);
                o.GuardTick();
                Assert.True(graceScheduler.IsPlaying && stalled.Grace.IsVisible);
                clock.Advance(TimeSpan.FromSeconds(61));
                o.GraceTick();
                Assert.False(o.GracePaused || stalled.Grace.IsVisible);
                Assert.False(card.IsVisible);
                Assert.InRange(stalled.Window.Opacity, double.Epsilon, 0.001);
                Assert.False(stalled.Window.IsHitTestVisible);
                Assert.Equal(0, o.PlayerVolume);
                o.GuardTick();
                Assert.False(graceScheduler.IsPlaying);
                Assert.Empty(o.Windows);
                Assert.False(stalled.Window.IsVisible);

                // Panic during the pause still closes everything, the separate card included.
                clock.Advance(TimeSpan.FromSeconds(1));
                Assert.True(graceScheduler.Trigger(strictOverride: false, path: clip));
                o.Show(clip, strict: false);
                var panicked = Assert.Single(o.Surfaces);
                Assert.True(o.TryGracePause(fromPanicKey: true));
                card = AssertCentredCard(panicked);
                graceScheduler.ForceCleanup();
                Assert.Empty(o.Windows);
                Assert.False(o.GracePaused);
                Assert.False(card.IsVisible || panicked.Window.IsVisible);
                (o.Scheduler, o.Time) = previous;

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
                graceScheduler.ForceCleanup();
                (o.Scheduler, o.Time) = previous;
                graceStamp.SetValue(o, previousStamp);
                o.CloseAll();
                (s.MandatoryVideoFloating, s.AttentionChecksEnabled, s.AudioDuckingEnabled, s.MandatoryVideoFloatingSizePercent,
                    s.PanicOverridesAll, s.MasterVolume, s.VideoVolume) = saved;
            }
            return Task.CompletedTask;
        });
    }

    // WPF GracePauseOverlayWindow: its own 360x200 window centred on the screen, the video stays veiled.
    private static Window AssertCentredCard(MandatoryVideoOverlay.Surface x)
    {
        Assert.InRange(x.Window.Opacity, double.Epsilon, 0.001);
        Assert.False(x.Window.IsHitTestVisible);
        var card = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(x.Grace));
        Assert.NotSame(x.Window, card);
        Assert.True(card.IsVisible && card.Topmost && !card.ShowActivated);
        // No RunJobs: the missing test clip's queued error would end the clip. Show already laid the card out.
        Assert.Equal(new Size(360, 200), card.ClientSize);
        var scr = card.Screens.Primary ?? card.Screens.All[0];
        var b = scr.Bounds;
        Assert.Equal(new PixelPoint(b.X + (int)((b.Width - 360 * scr.Scaling) / 2), b.Y + (int)((b.Height - 200 * scr.Scaling) / 2)), card.Position);
        var resume = Assert.Single(x.Grace.GetVisualDescendants().OfType<Button>());
        Assert.True(resume.IsEffectivelyVisible && resume.IsEffectivelyEnabled);
        var at = resume.TranslatePoint(default, card)!.Value;
        Assert.True(resume.Bounds.Width > 0 && at.X >= 0 && at.Y >= 0
            && at.X + resume.Bounds.Width <= 360 && at.Y + resume.Bounds.Height <= 200, "Resume is wholly on the card");
        return card;
    }

    // Trigger marks the scheduler live, but Show is driven synchronously before any frame can reveal it.
    private sealed class HeldPreroll : TimeProvider
    {
        private DateTimeOffset _now = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new HeldTimer();
        private sealed class HeldTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
