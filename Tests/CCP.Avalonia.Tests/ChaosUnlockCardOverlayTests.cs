using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosUnlockCardOverlayTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static ChaosUnlockCardInfo Card => ChaosUnlockCardRules.ForLesson("silk_touch")!;

    [Fact]
    public async Task Real_lesson_event_reaches_overlay_and_stale_events_after_teardown_are_ignored() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var lessons = new System.Collections.Generic.HashSet<string>(ChaosMeta.State.LessonsComplete ?? new());
        var progress = new System.Collections.Generic.Dictionary<string, long>(ChaosMeta.State.LessonProgress ?? new());
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.BeginRun(); ChaosRunHost.StopTimerForTests();
            ChaosMeta.State.LessonsComplete?.Remove("silk_touch");
            ChaosMeta.State.LessonProgress?.Remove("silk_touch"); // a prior high-water mark would swallow the raise
            ChaosLessons.RaiseTo("silk_touch", ChaosLessons.T_SILK_TOUCH);
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Assert.True(ChaosUnlockCardOverlay.IsShowing);
            Assert.True(BubbleOverlay.Field.HoldChaos);
            ChaosMeta.State.LessonsComplete?.Remove("popup_notification");
            ChaosMeta.State.LessonProgress?.Remove("popup_notification"); // a prior high-water mark would swallow the raise
            ChaosLessons.Tick("popup_notification", ChaosLessons.T_POPUP_NOTIF);
            ChaosRunHost.ForceShutdown();
            // A new run before the queued event drains: only the generation guard drops it.
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.BeginRun(); ChaosRunHost.StopTimerForTests();
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosMeta.State.LessonsComplete = lessons; ChaosMeta.State.LessonProgress = progress; ChaosMeta.Save();
        }
    });

    [Fact]
    public async Task Art_resolves_convention_and_explicit_paths_and_missing_art_keeps_glyph() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var info = Card with { IconFolder = "boons", IconId = "test" };
            ChaosArt.RootsOverride = () => new[] { root };
            var fallback = ChaosUnlockCardLayer.Build(info, 420).Root;
            Assert.Empty(fallback.GetLogicalDescendants().OfType<Image>());
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "boons"));
            string file = System.IO.Path.Combine(root, "boons", "test.png");
            using (var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
                bitmap.Save(file);
            Assert.Single(ChaosUnlockCardLayer.Build(info, 420).Root.GetLogicalDescendants().OfType<Image>());
            Assert.Single(ChaosUnlockCardLayer.Build(info with { IconPath = file }, 420).Root.GetLogicalDescendants().OfType<Image>());
            Assert.Null(ChaosArt.FilePath("../test.png"));
        }
        finally { ChaosArt.ResetForTest(); System.IO.Directory.Delete(root, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Queue_click_timeout_and_keep_alive_match_Wpf() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        try
        {
            int callbacks = 0;
            ChaosUnlockCardOverlay.Show(Card, () => { callbacks++; Assert.True(ChaosUnlockCardOverlay.IsShowing); }, false);
            var w = ChaosUnlockCardOverlay.Active!;
            double now = 0; w.Now = () => now;
            // Reopen on the stepped clock.
            ChaosUnlockCardOverlay.CloseActive();
            ChaosUnlockCardOverlay.Show(Card, () => { callbacks++; Assert.True(ChaosUnlockCardOverlay.IsShowing); }, false);
            w = ChaosUnlockCardOverlay.Active!;
            double start = w.Now(); w.Now = () => start + now;
            Assert.Contains(((Control)w.Content!).GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "click to continue");
            now = 20000; w.Advance();
            Assert.True(ChaosUnlockCardOverlay.IsShowing); // paused: no timeout
            Assert.Equal(1, w.Opacity);
            ChaosUnlockCardOverlay.Show(Card, () => callbacks++);
            w.Dismiss();
            w.Advance();
            Assert.Equal(1, w.Opacity); // direct swap: no scrim blink
            Assert.Equal(1, callbacks);
            Assert.Same(w, ChaosUnlockCardOverlay.Active);
            now += ChaosUnlockCardOverlay.AutoMs; w.Advance();
            now += ChaosUnlockCardOverlay.OutMs; w.Advance();
            Assert.Equal(2, callbacks);
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
            Assert.False(w.IsVisible);
            ChaosUnlockCardOverlay.Show(Card, () => callbacks++);
            Assert.Same(w, ChaosUnlockCardOverlay.Active);
            ChaosUnlockCardOverlay.CloseActive();
            Assert.Equal(2, callbacks); // teardown drops callbacks
        }
        finally { ChaosUnlockCardOverlay.CloseActive(); }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Card_arriving_during_fade_waits_and_panic_drops_it() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        try
        {
            bool done = false;
            ChaosUnlockCardOverlay.Show(Card);
            var w = ChaosUnlockCardOverlay.Active!;
            double now = w.Now(); w.Now = () => now;
            w.Dismiss();
            ChaosUnlockCardOverlay.Show(Card, () => done = true);
            now += ChaosUnlockCardOverlay.OutMs; w.Advance();
            Assert.True(ChaosUnlockCardOverlay.IsShowing);
            Assert.Same(w, ChaosUnlockCardOverlay.Active);
            PanicSurfaces.All.Single(s => s.Id == "chaos-unlock-card").Stop(null);
            Assert.False(done);
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
            Assert.Null(ChaosUnlockCardOverlay.Active);
        }
        finally { ChaosUnlockCardOverlay.CloseActive(); }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Lessons_hold_clock_field_and_toys_until_last_click_without_stealing_manual_pause() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.BeginRun(); ChaosRunHost.StopTimerForTests();
            ChaosRunHost.OnLessonCompleted("silk_touch");
            ChaosRunHost.OnLessonCompleted("popup_notification");
            Assert.True(BubbleOverlay.Field.HoldChaos);
            ChaosRunHost.Tick();
            Assert.Equal(0, ChaosRunHost.State!.ElapsedSec);
            ChaosRunHost.ToggleManualPause();
            Assert.False(ChaosRunHost.IsManuallyPaused);
            var w = ChaosUnlockCardOverlay.Active!;
            w.Dismiss();
            Assert.True(BubbleOverlay.Field.HoldChaos);
            double now = w.Now(); w.Now = () => now;
            w.Dismiss(); now += ChaosUnlockCardOverlay.OutMs; w.Advance();
            Assert.False(BubbleOverlay.Field.HoldChaos);
            ChaosRunHost.Tick();
            Assert.True(ChaosRunHost.State.ElapsedSec > 0);
            ChaosRunHost.ToggleManualPause();
            ChaosRunHost.OnLessonCompleted("silk_touch");
            w = ChaosUnlockCardOverlay.Active!;
            now = w.Now(); w.Now = () => now;
            now += ChaosUnlockCardOverlay.AutoMs; w.Advance();
            now += ChaosUnlockCardOverlay.OutMs; w.Advance();
            Assert.True(ChaosRunHost.IsManuallyPaused);
            Assert.True(BubbleOverlay.Field.HoldChaos);
        }
        finally { ChaosRunHost.ForceShutdown(); }
        return Task.CompletedTask;
    });
}
