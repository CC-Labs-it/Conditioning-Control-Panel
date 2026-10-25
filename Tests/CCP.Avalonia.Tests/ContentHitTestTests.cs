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
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Keincheck's hit_test put points in the tab content on the window template's Panel. This
/// proves real pointer input in the content area at the default size reaches the control drawn
/// there: every visible, enabled, unclipped button in four tabs is the hit-test result at its own
/// centre and receives a headless press there. The press is swallowed at the button's tunnel so no
/// click side effect runs.</summary>
// The render barrier pumps the shared dispatcher: another collection's cleanup must not
// close this shell or restore process state while its pointer scene is being probed.
[Collection(RunsAloneCollection.Name)]
public sealed class ContentHitTestTests
{
    [Fact]
    public async Task ContentButtonsReceiveThePointerAtTheirCentre()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            // P52/P76: ShowTab("studio"/"awareness") queues a modal feature intro whose showing depends on
            // what earlier tests spent (door slots, cooldown); none may open over the probed shell.
            CoreSettings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            var w = new MainShellWindow();
            try
            {
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
                    HeadlessRender.LiveTree(w);
                    int tested = 0;
                    foreach (var b in w.Named<Control>(panelName)!.GetVisualDescendants().OfType<Button>().ToList())
                    {
                        // Hit testing reads the compositor's last frame: a tab shown (or a press drawn) since then is not in it yet.
                        HeadlessRender.LiveTree(w);
                        if (!b.IsEffectivelyVisible || !b.IsEffectivelyEnabled || b.Bounds.Width < 4 || b.TemplatedParent is ScrollBar) continue;
                        // Click-proof on purpose (WPF Hidden): only the Presets rack row actions at rest; their hover path has its own test.
                        if (b.GetVisualAncestors().OfType<StackPanel>().Any(s => s.Classes.Contains("rackActions") && !s.IsHitTestVisible)) continue;
                        var p = Centre(b, w);
                        // The barrier's last RunJobs can run a layout pass after the awaited batch rendered,
                        // leaving the live tree one batch ahead of the frame the hit test reads. Probe only
                        // once the centre is unchanged across a further render.
                        for (var settle = Stopwatch.StartNew(); ;)
                        {
                            HeadlessRender.LiveTree(w);
                            var q = Centre(b, w);
                            if (q == p) break;
                            Assert.True(settle.Elapsed < TimeSpan.FromSeconds(10), $"{tab} {b.Name}: centre still moving ({p} -> {q})");
                            p = q;
                        }
                        if (!new Rect(w.Bounds.Size).Contains(p) || b.GetVisualAncestors().OfType<ScrollViewer>().Any(s => !Inside(s, p, w))) continue;
                        if (w.GetVisualsAt(p).OfType<Button>().FirstOrDefault(o => o != b && !b.IsVisualAncestorOf(o)) is { } over && !over.IsVisualAncestorOf(b)) continue;   // another control drawn on top, e.g. the Start footer
                        tested++;
                        var hit = w.InputHitTest(p) as Visual;
                        bool pressed = false;
                        void Swallow(object? s, PointerPressedEventArgs e) { pressed = true; e.Handled = true; }
                        b.AddHandler(InputElement.PointerPressedEvent, Swallow, RoutingStrategies.Tunnel);
                        w.MouseDown(p, MouseButton.Left);
                        w.MouseUp(p, MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                        b.RemoveHandler(InputElement.PointerPressedEvent, (System.EventHandler<PointerPressedEventArgs>)Swallow);
                        if (hit is null || (hit != b && !b.IsVisualAncestorOf(hit)) || !pressed)
                        {
                            // Still a miss; the re-render only records whether a later frame would have held it.
                            HeadlessRender.LiveTree(w);
                            var again = w.InputHitTest(p) as Visual;
                            misses.Add($"{tab} {b.Name ?? b.GetType().Name} @{p}: hit {hit?.GetType().Name}/{(hit as Control)?.Name}, pressed {pressed}"
                                + $"; after one more render: centre {Centre(b, w)}, hit {again?.GetType().Name}/{(again as Control)?.Name}, owned windows {w.OwnedWindows.Count}");
                        }
                    }
                    Assert.True(tested >= 3, $"{tab}: only {tested} buttons on screen to test");
                }
                Assert.True(misses.Count == 0, string.Join("\n", misses));
            }
            finally
            {
                w.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }

    private static Point Centre(Control b, Visual w) => b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;

    private static bool Inside(Visual v, Point p, Visual root)
        => v.TranslatePoint(default, root) is { } tl && v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), root) is { } br
           && new Rect(tl, br).Contains(p);
}
