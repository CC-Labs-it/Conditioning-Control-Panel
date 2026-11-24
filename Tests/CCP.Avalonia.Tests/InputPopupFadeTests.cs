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

public sealed class InputPopupFadeTests
{
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
    public Task CancelledSubmenuFadeStillClosesThePopup() => Run(async window =>
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
        runs.Last().Done.SetCanceled();
        await InputPopupFx.Pending(popup);
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public Task PanicClosesFadingPopup() => Run(async window =>
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
        Assert.True(popup.IsOpen);
        PanicSurfaces.All.Single(s => s.Id == "input-popups").Stop(null);
        Assert.False(popup.IsOpen);
        await InputPopupFx.Pending(popup);
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
