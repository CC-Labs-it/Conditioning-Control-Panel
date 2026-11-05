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
            // This probes static hit geometry, not tab entrance or hover animation progress.
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            var w = new MainShellWindow();
            try
            {
                var sinceShow = Stopwatch.StartNew();
                w.ShowTab(tab);
                // The entrance (fade, slide, card stagger) runs on wall time; land it so runner speed cannot put a
                // probe mid-entrance. Suspected, not reproduced: CI misses only ever hit the first awareness probes,
                // inside the 620 ms entrance (hero hit ScrollContentPresenter, Windows CI 2026-10-30).
                w.CompleteTabTransition();
                // The logged-out cover is WPF behaviour (MainWindow.Login.cs:378); test the tab under it.
                if (tab == "quests") w.Named<Control>(panelName)!.FindControl<Border>("QuestsLoginOverlay")!.IsVisible = false;
                // Same for the free-tier veil (WPF MainWindow.Patreon.cs:63 RefreshPremiumGate).
                if (tab == "awareness") w.Named<Control>(panelName)!.FindControl<Border>("AwarenessGate")!.IsVisible = false;
                HeadlessRendering.RenderLiveTree(w);
                int tested = 0;
                foreach (var b in w.Named<Control>(panelName)!.GetVisualDescendants().OfType<Button>().ToList())
                {
                    // Hit testing reads the compositor's last frame: a tab shown (or a press drawn) since then is not in it yet.
                    HeadlessRendering.RenderLiveTree(w);
                    // !IsHitTestVisible: inert until hovered by design (the privacy card's chip close, WPF the same).
                    if (!b.IsEffectivelyVisible || !b.IsEffectivelyEnabled || !b.IsHitTestVisible || b.Bounds.Width < 4 || b.TemplatedParent is ScrollBar) continue;
                    // Click-proof on purpose (WPF Hidden): only the Presets rack row actions at rest; their hover path has its own test.
                    if (b.GetVisualAncestors().OfType<StackPanel>().Any(s => s.Classes.Contains("rackActions") && !s.IsHitTestVisible)) continue;
                    var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
                    if (!new Rect(w.Bounds.Size).Contains(p) || b.GetVisualAncestors().OfType<ScrollViewer>().Any(s => !Inside(s, p, w))) continue;
                    if (w.GetVisualsAt(p).OfType<Button>().FirstOrDefault(o => o != b && !b.IsVisualAncestorOf(o)) is { } over && !over.IsVisualAncestorOf(b)) continue;   // another control drawn on top, e.g. the Start footer
                    tested++;
                    var hit = w.InputHitTest(p) as Visual;
                    // On a recurrence, report timing and the visuals held in the rendered frame.
                    var probe = $"{sinceShow.ElapsedMilliseconds} ms after ShowTab, under {string.Join(">", w.GetVisualsAt(p).Take(6).Select(v => $"{v.GetType().Name}/{(v as Control)?.Name}"))}";
                    bool pressed = false;
                    void Swallow(object? s, PointerPressedEventArgs e) { pressed = true; e.Handled = true; }
                    b.AddHandler(InputElement.PointerPressedEvent, Swallow, RoutingStrategies.Tunnel);
                    w.MouseDown(p, MouseButton.Left);
                    w.MouseUp(p, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                    b.RemoveHandler(InputElement.PointerPressedEvent, (System.EventHandler<PointerPressedEventArgs>)Swallow);
                    if (hit is null || (hit != b && !b.IsVisualAncestorOf(hit)) || !pressed)
                        misses.Add($"{tab} {b.Name ?? b.GetType().Name} @{p}: hit {hit?.GetType().Name}/{(hit as Control)?.Name}, pressed {pressed}; {probe}");
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

    private static bool Inside(Visual v, Point p, Visual root)
        => v.TranslatePoint(default, root) is { } tl && v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), root) is { } br
           && new Rect(tl, br).Contains(p);
}
