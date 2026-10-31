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
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var w = new MainShellWindow();
            // The pointer rests on each button after its press; ToolTipService opens that row's tooltip on
            // a real 400 ms timer, and on a slow runner it covered the next rows (hit ToolTip's Border
            // PART_LayoutRoot, Windows CI 38136837018). Tooltips are not what this test is about.
            ToolTip.SetServiceEnabled(w, false);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var misses = new List<string>();
            foreach (var (tab, panelName) in new[] { ("studio", "StudioTab"), ("presets", "PresetsTab"), ("awareness", "AwarenessTab"), ("quests", "QuestsTab") })
            {
                w.ShowTab(tab);
                // The logged-out cover is WPF behaviour (MainWindow.Login.cs:378); test the tab under it.
                if (tab == "quests") w.Named<Control>(panelName)!.FindControl<Border>("QuestsLoginOverlay")!.IsVisible = false;
                // Same for the free-tier veil (WPF MainWindow.Patreon.cs:63 RefreshPremiumGate).
                if (tab == "awareness") w.Named<Control>(panelName)!.FindControl<Border>("AwarenessGate")!.IsVisible = false;
                RenderLiveTree(w);
                int tested = 0;
                foreach (var b in w.Named<Control>(panelName)!.GetVisualDescendants().OfType<Button>().ToList())
                {
                    // Hit testing reads the compositor's last frame: a tab shown (or a press drawn) since then is not in it yet.
                    RenderLiveTree(w);
                    // !IsHitTestVisible: inert until hovered by design (the privacy card's chip close, WPF the same).
                    if (!b.IsEffectivelyVisible || !b.IsEffectivelyEnabled || !b.IsHitTestVisible || b.Bounds.Width < 4 || b.TemplatedParent is ScrollBar) continue;
                    // Click-proof on purpose (WPF Hidden): only the Presets rack row actions at rest; their hover path has its own test.
                    if (b.GetVisualAncestors().OfType<StackPanel>().Any(s => s.Classes.Contains("rackActions") && !s.IsHitTestVisible)) continue;
                    var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
                    if (!new Rect(w.Bounds.Size).Contains(p) || b.GetVisualAncestors().OfType<ScrollViewer>().Any(s => !Inside(s, p, w))) continue;
                    if (w.GetVisualsAt(p).OfType<Button>().FirstOrDefault(o => o != b && !b.IsVisualAncestorOf(o)) is { } over && !over.IsVisualAncestorOf(b)) continue;   // another control drawn on top, e.g. the Start footer
                    tested++;
                    var (hit, hitButton, pressed) = PressButton(w, b, p);
                    if (!hitButton || !pressed)
                        misses.Add($"{tab} {b.Name ?? b.GetType().Name} @{p}: hit {hit?.GetType().Name}/{(hit as Control)?.Name}, pressed {pressed}");
                }
                Assert.True(tested >= 3, $"{tab}: only {tested} buttons on screen to test");
            }
            w.Close();
            Assert.True(misses.Count == 0, string.Join("\n", misses));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task APressCanReplaceTheHitVisualAfterItWasValidated() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var content = new Border { Width = 60, Height = 30, Background = Brushes.Red };
        var button = new Button { Content = content, Width = 100, Height = 60 };
        var window = new Window { Content = button, Width = 180, Height = 120 };
        try
        {
            window.Show();
            RenderLiveTree(window);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            var ready = new Border { Width = 60, Height = 30, Background = Brushes.Green };
            Dispatcher.UIThread.Post(() => { button.Content = ready; window.UpdateLayout(); });
            button.AddHandler(InputElement.PointerPressedEvent,
                (_, _) =>
                {
                    button.Content = new Border { Width = 60, Height = 30, Background = Brushes.Blue };
                    window.UpdateLayout();
                }, RoutingStrategies.Tunnel, handledEventsToo: true);
            var (hit, hitButton, pressed) = PressButton(window, button, point);
            Assert.Same(ready, hit);
            Assert.True(hitButton);
            Assert.True(pressed);
            Assert.False(button.IsVisualAncestorOf(ready));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static (Visual? Hit, bool HitButton, bool Pressed) PressButton(Window window, Button button, Point point)
    {
        // MouseDown pumps jobs and rendering before dispatch. Do the same before the
        // independent hit query, so it observes the frame the press will use.
        using (window.CaptureRenderedFrame()) { }
        RenderLiveTree(window);
        var hit = window.InputHitTest(point) as Visual;
        // Validate against the tree queried by InputHitTest. Pointer handlers and queued bindings
        // can replace template/content visuals, detaching that hit before the assertion runs.
        var hitButton = hit is not null && (hit == button || button.IsVisualAncestorOf(hit));
        bool pressed = false;
        void Swallow(object? s, PointerPressedEventArgs e) { pressed = true; e.Handled = true; }
        button.AddHandler(InputElement.PointerPressedEvent, Swallow, RoutingStrategies.Tunnel);
        try
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        finally { button.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Swallow); }
        return (hit, hitButton, pressed);
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
