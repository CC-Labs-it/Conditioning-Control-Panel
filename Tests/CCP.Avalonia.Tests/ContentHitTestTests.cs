using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Keincheck's hit_test put points in the tab content on the window template's Panel. This
/// proves real pointer input in the content area at the default size reaches the control drawn
/// there: every visible, enabled, unclipped button in four tabs is the hit-test result at its own
/// centre and receives a headless press there. The press is swallowed at the button's tunnel so no
/// click side effect runs.</summary>
public sealed class ContentHitTestTests
{
    [Fact]
    public async Task ContentButtonsReceiveThePointerAtTheirCentre()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var settings = CoreSettings.Current;
            var old = (settings.MotionLevel, settings.GentleVisuals);
            var clock = new SteppedClock();
            settings.MotionLevel = MotionLevel.Full;
            settings.GentleVisuals = false;
            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow { TabClock = clock };
                // Tooltips can cover the next row on a slow runner; they have their own tests.
                ToolTip.SetServiceEnabled(w, false);
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var misses = new List<string>();
                foreach (var (tab, panelName) in new[] { ("studio", "StudioTab"), ("presets", "PresetsTab"), ("awareness", "AwarenessTab"), ("quests", "QuestsTab") })
                {
                    w.ShowTab(tab);
                    // Test beneath WPF's logged-out and free-tier veils (Login.cs:378, Patreon.cs:63).
                    if (tab == "quests") w.Named<Control>(panelName)!.FindControl<Border>("QuestsLoginOverlay")!.IsVisible = false;
                    if (tab == "awareness") w.Named<Control>(panelName)!.FindControl<Border>("AwarenessGate")!.IsVisible = false;
                    // Probe all controls during a fixed entrance frame AND after it settles.
                    clock.Now += TimeSpan.FromMilliseconds(60).Ticks;
                    w.TickTabTransition();
                    RenderLiveTree(w);
                    ProbeContentButtons(w, tab, panelName, "entrance", misses);
                    PrepareTabForHitTest(w);
                    ProbeContentButtons(w, tab, panelName, "settled", misses);
                }
                Assert.True(misses.Count == 0, string.Join("\n", misses));
            }
            finally
            {
                try { w?.Close(); }
                finally { (settings.MotionLevel, settings.GentleVisuals) = old; }
            }
            return Task.CompletedTask;
        });
    }

    private static void ProbeContentButtons(MainShellWindow w, string tab, string panelName, string phase, List<string> misses)
    {
        int tested = 0;
        foreach (var b in w.Named<Control>(panelName)!.GetVisualDescendants().OfType<Button>().ToList())
        {
            RenderLiveTree(w);
            if (!b.IsEffectivelyVisible || !b.IsEffectivelyEnabled || !b.IsHitTestVisible || b.Bounds.Width < 4 || b.TemplatedParent is ScrollBar) continue;
            if (b.GetVisualAncestors().OfType<StackPanel>().Any(s => s.Classes.Contains("rackActions") && !s.IsHitTestVisible)) continue;
            var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
            if (!new Rect(w.Bounds.Size).Contains(p) || b.GetVisualAncestors().OfType<ScrollViewer>().Any(s => !Inside(s, p, w))) continue;
            if (w.GetVisualsAt(p).OfType<Button>().FirstOrDefault(o => o != b && !b.IsVisualAncestorOf(o)) is { } over && !over.IsVisualAncestorOf(b)) continue;
            tested++;
            var (hit, hitInside, pressed) = ProbeButton(w, b, p);
            if (!hitInside || !pressed)
                misses.Add($"{tab} ({phase}) {b.Name ?? b.GetType().Name} @{p}: hit {hit?.GetType().Name}/{(hit as Control)?.Name}, pressed {pressed}");
        }
        Assert.True(tested >= 3, $"{tab} ({phase}): only {tested} buttons on screen to test");
    }

    [Theory]
    [InlineData(60)]
    [InlineData(160)]
    public Task AwarenessTutorialReceivesPointerDuringTabEntrance(int milliseconds) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.GentleVisuals);
        var clock = new SteppedClock();
        var w = new MainShellWindow { TabClock = clock };
        try
        {
            settings.MotionLevel = MotionLevel.Full;
            settings.GentleVisuals = false;
            ToolTip.SetServiceEnabled(w, false);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.ShowTab("awareness");
            var tab = w.Named<Control>("AwarenessTab")!;
            tab.FindControl<Border>("AwarenessGate")!.IsVisible = false;
            clock.Now = TimeSpan.FromMilliseconds(milliseconds).Ticks;
            w.TickTabTransition();
            RenderLiveTree(w);
            var button = tab.FindControl<Button>("BtnAwarenessTutorial")!;
            var p = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), w)!.Value;
            var hit = w.InputHitTest(p) as Visual;
            Assert.True(hit == button || (hit != null && button.IsVisualAncestorOf(hit)), $"{milliseconds} ms: hit {hit}");
            bool pressed = false;
            void Swallow(object? sender, PointerPressedEventArgs e) { pressed = true; e.Handled = true; }
            button.AddHandler(InputElement.PointerPressedEvent, Swallow, RoutingStrategies.Tunnel);
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            button.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Swallow);
            Assert.True(pressed, $"{milliseconds} ms: tutorial button did not receive the press");
        }
        finally { w.Close(); (settings.MotionLevel, settings.GentleVisuals) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HitProbeRemainsStableWhenTheEntranceClockAdvances() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.GentleVisuals);
        var clock = new SteppedClock();
        var w = new MainShellWindow { TabClock = clock };
        try
        {
            settings.MotionLevel = MotionLevel.Full;
            settings.GentleVisuals = false;
            ToolTip.SetServiceEnabled(w, false);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.ShowTab("awareness");
            var tab = w.Named<Control>("AwarenessTab")!;
            tab.FindControl<Border>("AwarenessGate")!.IsVisible = false;
            clock.Now = TimeSpan.FromMilliseconds(60).Ticks;
            w.TickTabTransition();
            PrepareTabForHitTest(w);
            var button = tab.FindControl<Button>("BtnAwarenessTutorial")!;
            var slide = Assert.IsType<TranslateTransform>(tab.RenderTransform);
            double x = slide.X > 0.5 ? button.Bounds.Width - 1 : slide.X < -0.5 ? 1 : button.Bounds.Width / 2;
            double y = slide.Y > 0.5 ? button.Bounds.Height - 1 : slide.Y < -0.5 ? 1 : button.Bounds.Height / 2;
            var p = button.TranslatePoint(new Point(x, y), w)!.Value;
            clock.Now = TimeSpan.FromMilliseconds(200).Ticks;
            w.TickTabTransition();
            RenderLiveTree(w);
            var hit = w.InputHitTest(p) as Visual;
            Assert.True(hit == button || (hit != null && button.IsVisualAncestorOf(hit)), $"the probe drifted onto {hit}");
        }
        finally { w.Close(); (settings.MotionLevel, settings.GentleVisuals) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HitProbeCapturesAncestryBeforePointerDispatchReplacesContent() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var original = new Border { Width = 120, Height = 30, Background = Brushes.Transparent };
        var button = new Button { Content = original, Width = 160, Height = 50,
                                  HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                                  VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        var w = new Window { Width = 300, Height = 160, Content = button };
        try
        {
            w.Show();
            RenderLiveTree(w);
            var p = original.TranslatePoint(new Point(original.Bounds.Width / 2, original.Bounds.Height / 2), w)!.Value;
            var (hit, hitInside, pressed) = ProbeButton(w, button, p, () => button.Content = "replacement");
            RenderLiveTree(w);
            Assert.Same(original, hit);
            Assert.True(pressed);
            Assert.True(hitInside);
            Assert.False(button.IsVisualAncestorOf(original));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    private static (Visual? Hit, bool Inside, bool Pressed) ProbeButton(Window w, Button button, Point p, Action? onPress = null)
    {
        var hit = w.InputHitTest(p) as Visual;
        // Read ancestry with the hit: pointer dispatch can rebuild the visual tree.
        bool hitInside = hit != null && (hit == button || button.IsVisualAncestorOf(hit));
        bool pressed = false;
        void Swallow(object? sender, PointerPressedEventArgs e) { pressed = true; onPress?.Invoke(); e.Handled = true; }
        button.AddHandler(InputElement.PointerPressedEvent, Swallow, RoutingStrategies.Tunnel);
        try
        {
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        finally { button.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Swallow); }
        return (hit, hitInside, pressed);
    }

    private static void PrepareTabForHitTest(MainShellWindow w)
    {
        w.CompleteTabTransition();
        RenderLiveTree(w);
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
    }

    /// <summary>Renders a frame holding the live tree. One forced tick is not that: while a batch is in
    /// flight MediaContext holds the next commit back, so the tick renders the OLDER batch and the layout
    /// committed after it is missing from the frame. Every tab's first probe ran with one batch pending,
    /// and on a slow runner the stale frame still had no hero there (Linux CI 38316007639: awareness
    /// BtnAwarenessTutorial / ChkAwarenessMaster hit ScrollContentPresenter, pressed False).</summary>
    private static void RenderLiveTree(Visual w)
    {
        Dispatcher.UIThread.RunJobs();
        var batch = ElementComposition.GetElementVisual(w)!.Compositor.RequestCompositionBatchCommitAsync();
        var deadline = Stopwatch.StartNew();
        while (!batch.Rendered.IsCompleted)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the compositor never rendered the live tree");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static bool Inside(Visual v, Point p, Visual root)
        => v.TranslatePoint(default, root) is { } tl && v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), root) is { } br
           && new Rect(tl, br).Contains(p);
}
