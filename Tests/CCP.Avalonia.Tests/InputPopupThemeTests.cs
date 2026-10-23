using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InputPopupThemeTests
{
    [Fact]
    public Task ComboRowsFollowParentAlignmentAndStillAllowRowOverrides() => Run(window =>
    {
        var own = new ComboBoxItem { Content = "own", HorizontalContentAlignment = HorizontalAlignment.Left };
        var combo = new ComboBox
        {
            Width = 220, HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Bottom, Items = { "one", own }
        };
        window.Content = combo;
        window.Show();
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var row = (ComboBoxItem)combo.ContainerFromIndex(0)!;
        Assert.Equal(HorizontalAlignment.Right, row.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Bottom, row.VerticalContentAlignment);
        Assert.Equal(HorizontalAlignment.Right, Part<ContentPresenter>(row, "PART_ContentPresenter").HorizontalAlignment);
        combo.HorizontalContentAlignment = HorizontalAlignment.Center;
        combo.VerticalContentAlignment = VerticalAlignment.Top;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(HorizontalAlignment.Center, row.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Top, row.VerticalContentAlignment);
        Assert.Equal(HorizontalAlignment.Left, own.HorizontalContentAlignment);
        combo.IsDropDownOpen = false;
        return Task.CompletedTask;
    });

    [Fact]
    public Task ContextShadowAndNestedOffsetsMatchWpfWithoutMovingMenuBarPopups() => Run(window =>
    {
        var nested = new MenuItem { Header = "Nested", Items = { new MenuItem { Header = "Leaf" } } };
        var target = new Border { Width = 100, Height = 40 };
        var menu = new ContextMenu { Items = { nested } };
        target.ContextMenu = menu;
        var top = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Child" } } };
        window.Content = new StackPanel { Children = { new Menu { Items = { top } }, target } };
        window.Show();
        menu.Open(target);
        nested.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(menu.WindowManagerAddShadowHint);
        var popup = Part<Popup>(nested, "PART_Popup");
        Assert.Equal(PlacementMode.RightEdgeAlignedTop, popup.Placement);
        Assert.Equal(4, popup.HorizontalOffset);
        Assert.Equal(-6, popup.VerticalOffset);
        menu.Close();
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, Part<Popup>(top, "PART_Popup").Placement);
        top.IsSubMenuOpen = false;
        return Task.CompletedTask;
    });

    [Fact]
    public Task DeeperKeyedComboUsesItsExistingCustomRows() => Run(window =>
    {
        var editor = new DeeperEditorWindow();
        try
        {
            var combo = editor.FindControl<ComboBox>("CmbHapticPattern")!;
            combo.ApplyTemplate();
            var theme = (ControlTheme)editor.FindResource("EditorComboBoxItem")!;
            Assert.Same(theme, combo.ItemContainerTheme);
        }
        finally { editor.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ComboSlidesItsFullHeightAndClosesWithoutAnExitAnimation() => Run(async window =>
    {
        var runs = CaptureAnimations();
        var combo = new ComboBox { Width = 200, ItemsSource = new[] { "one", "two" } };
        window.Content = combo;
        window.Show();
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var popup = Part<Popup>(combo, "PART_Popup");
        Assert.Equal(InputPopupFx.Motion.Slide, InputPopupFx.GetAnimation(popup));
        var run = Assert.Single(runs);
        Assert.Same(popup.Child, run.Child); // animation target is the Visual, never the Transform
        Assert.Equal(TimeSpan.FromMilliseconds(150), run.Animation.Duration);
        Assert.Equal(TranslateTransform.YProperty, ((Setter)run.Animation.Children[0].Setters[0]).Property);
        Assert.Equal(run.Child.Bounds.Height, Math.Abs((double)((Setter)run.Animation.Children[0].Setters[0]).Value!));
        Assert.Equal(0d, ((Setter)run.Animation.Children[1].Setters[0]).Value);
        Assert.True(run.Child.GetVisualParent()!.ClipToBounds);
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
        Assert.Single(runs); // no close animation for WPF Slide
        await InputPopupFx.Pending(popup);
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        popup.Close(); // native close must also clear the owning combo's state
        Assert.False(combo.IsDropDownOpen);
    });

    [Fact]
    public Task SubmenuFadesBothWaysAndReopeningCancelsTheOldClose() => Run(async window =>
    {
        var runs = CaptureAnimations();
        var top = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Leaf" } } };
        window.Content = new Menu { Items = { top } };
        window.Show();
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var popup = Part<Popup>(top, "PART_Popup");
        Assert.Equal(InputPopupFx.Motion.Fade, InputPopupFx.GetAnimation(popup));
        var enter = Assert.Single(runs);
        AssertFade(enter.Animation, 0, 1);
        enter.Done.SetResult();
        await InputPopupFx.Pending(popup);
        top.IsSubMenuOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen); // keep only the visuals for WPF's 150ms fade
        Assert.False(popup.Child!.IsHitTestVisible);
        var exit = runs.Last();
        AssertFade(exit.Animation, 1, 0);
        var oldClose = InputPopupFx.Pending(popup);
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        await oldClose;
        exit.Done.SetResult(); // stale completion cannot close the reopened popup
        Assert.True(popup.IsOpen);
        Assert.True(popup.Child.IsHitTestVisible);
        runs.Last().Done.SetResult();
        await InputPopupFx.Pending(popup);
        top.IsSubMenuOpen = false;
        Dispatcher.UIThread.RunJobs();
        runs.Last().Done.SetResult();
        await InputPopupFx.Pending(popup);
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public Task PanicOwnerCloseAndThemeReplacementCancelPendingPopupWork() => Run(async window =>
    {
        var runs = CaptureAnimations();
        var combo = new ComboBox { Width = 200, ItemsSource = new[] { "one" } };
        var top = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Leaf" } } };
        window.Content = new StackPanel { Children = { combo, new Menu { Items = { top } } } };
        window.Show();
        combo.IsDropDownOpen = true;
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var popup = Part<Popup>(top, "PART_Popup");
        top.IsSubMenuOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        PanicSurfaces.All.Single(s => s.Id == "input-popups").Stop(null);
        Assert.False(popup.IsOpen);
        Assert.False(combo.IsDropDownOpen);
        await InputPopupFx.Pending(popup);

        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var comboPopup = Part<Popup>(combo, "PART_Popup");
        combo.Theme = (ControlTheme)Application.Current!.FindResource(typeof(ComboBox))!;
        combo.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        Assert.False(comboPopup.IsOpen);
        Assert.Equal(InputPopupFx.Motion.None, InputPopupFx.GetAnimation(Part<Popup>(combo, "PART_Popup")));
        await InputPopupFx.Pending(comboPopup);
        combo.IsDropDownOpen = false;
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        window.Close();
        Dispatcher.UIThread.RunJobs();
        await InputPopupFx.Pending(popup);
        Assert.False(popup.IsOpen);
        foreach (var run in runs) run.Done.TrySetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public Task FaultedSubmenuFadeStillClosesThePopup() => Run(async window =>
    {
        var runs = CaptureAnimations();
        var top = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Leaf" } } };
        window.Content = new Menu { Items = { top } };
        window.Show();
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var popup = Part<Popup>(top, "PART_Popup");
        runs.Last().Done.SetResult();
        await InputPopupFx.Pending(popup);
        top.IsSubMenuOpen = false;
        Dispatcher.UIThread.RunJobs();
        runs.Last().Done.SetException(new InvalidOperationException("animation broke"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InputPopupFx.Pending(popup));
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public Task RealAnimationRunnerDrivesTheSubmenuVisualOnARenderTick() => Run(window =>
    {
        // P08: the headless render timer reads a Stopwatch and Animatable.Clock is internal, so the real
        // 150ms run cannot be stepped to its end; prove it starts on the Visual. Completion paths above.
        var top = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Leaf" } } };
        window.Content = new Menu { Items = { top } };
        window.Show();
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var popup = Part<Popup>(top, "PART_Popup");
        Assert.False(InputPopupFx.Pending(popup).IsFaulted);
        Assert.True(popup.Child!.Opacity < 1, $"fade-in not applied, opacity {popup.Child.Opacity}");
        return Task.CompletedTask;
    });

    private sealed record AnimationRun(Animation Animation, Control Child, TaskCompletionSource Done);
    private static List<AnimationRun> CaptureAnimations()
    {
        var runs = new List<AnimationRun>();
        // Avalonia 12's animation clock is internal. Complete the real user path deterministically;
        // assert its 150ms animation configuration separately, without wall-clock timing claims.
        InputPopupFx.RunAnimation = (animation, child, token) =>
        {
            var done = new TaskCompletionSource();
            runs.Add(new AnimationRun(animation, child, done));
            return done.Task.WaitAsync(token);
        };
        return runs;
    }
    private static void AssertFade(Animation animation, double from, double to)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(150), animation.Duration);
        Assert.Equal(Visual.OpacityProperty, ((Setter)animation.Children[0].Setters[0]).Property);
        Assert.Equal(from, ((Setter)animation.Children[0].Setters[0]).Value);
        Assert.Equal(to, ((Setter)animation.Children[1].Setters[0]).Value);
    }

    private static T Part<T>(Visual host, string name) where T : Control =>
        host.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static Task Run(Func<Window, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new Window { Width = 500, Height = 400 };
        var runner = InputPopupFx.RunAnimation;
        try { await body(window); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); InputPopupFx.RunAnimation = runner; }
    });
}
