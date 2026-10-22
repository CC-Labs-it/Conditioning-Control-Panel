using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;
namespace CCP.Avalonia.Tests;

public sealed class SharedDeeperThemeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static ControlTheme Theme(string key) => (ControlTheme)Application.Current!.FindResource(key)!;
    private static Border Plate(Control control) => control.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "bd");

    [Fact]
    public Task RoundButtonsKeepCallerColorsAndMatchHoverPressDisabled() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        foreach (var (key, radius) in new[] { ("DeeperRoundButton", 8), ("DeeperRoundIconButton", 6) })
        {
            var button = new Button { Theme = Theme(key), Content = "Test", Width = 120, Height = 40,
                Background = Brushes.Red, BorderBrush = Brushes.Blue, BorderThickness = new Thickness(2), Padding = new Thickness(9), FontSize = 17 };
            var window = new Window { Content = button, Width = 200, Height = 100 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var plate = Plate(button);
                Assert.Equal(new CornerRadius(radius), plate.CornerRadius);
                Assert.Same(button.Background, plate.Background);
                Assert.Same(button.BorderBrush, plate.BorderBrush);
                Assert.Equal(button.Padding, plate.Padding);
                Assert.Equal(17, button.FontSize);
                window.MouseMove(new Point(100, 50)); Dispatcher.UIThread.RunJobs();
                Assert.Equal(0.85, plate.Opacity);
                window.MouseDown(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.Equal(0.70, plate.Opacity);
                window.MouseUp(new Point(100, 50), MouseButton.Left);
                button.IsEnabled = false; Dispatcher.UIThread.RunJobs();
                Assert.Equal(0.40, plate.Opacity);
            }
            finally { window.Close(); }
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task CheckedIconUsesTheAccentAndSixPixelCorners() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var toggle = new ToggleButton { Theme = Theme("DeeperRoundIconToggleButton"), Content = "Lock", Background = Brushes.Transparent };
        var window = new Window { Content = toggle, Width = 200, Height = 100 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(new CornerRadius(6), Plate(toggle).CornerRadius);
            Assert.Same(toggle.Background, Plate(toggle).Background);
            window.MouseDown(new Point(100, 50), MouseButton.Left);
            window.MouseUp(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsChecked);
            Assert.Equal(Application.Current!.FindResource("DeeperAccentTransparent20Brush"), Plate(toggle).Background);
            Assert.Equal(0.85, Plate(toggle).Opacity);
        }
        finally { window.Close(); }
        await Task.CompletedTask;
    });

    [Fact]
    public Task ActionSecondaryOutlinePressIn80msAndReleaseWithBackEase03() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var release = (Transitions)Application.Current!.FindResource("PressReleaseTransitions")!;
        var press = (Transitions)Application.Current!.FindResource("PressDownTransitions")!;
        var releaseTween = Assert.IsType<TransformOperationsTransition>(Assert.Single(release));
        Assert.Equal(TimeSpan.FromMilliseconds(150), releaseTween.Duration);
        Assert.Equal(0.3, Assert.IsType<global::ConditioningControlPanel.Avalonia.Theme.BackEaseOutAmplitude>(releaseTween.Easing).Amplitude);
        Assert.Equal(TimeSpan.FromMilliseconds(80), Assert.IsType<TransformOperationsTransition>(Assert.Single(press)).Duration);
        foreach (var key in new[] { "ActionButton", "SecondaryButton", "OutlineButton" })
        {
            var button = new Button { Theme = Theme(key), Content = "Test", Width = 120, Height = 40 };
            var window = new Window { Content = button, Width = 200, Height = 100 };
            window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var border = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "border");
                Assert.Same(release, border.Transitions);
                window.MouseDown(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.Same(press, border.Transitions);
                window.MouseUp(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.Same(release, border.Transitions);
            }
            finally { window.Close(); }
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task EditorPlayerAndHubUseTheSharedThemes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldMotion = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio }, null);
        var player = new EnhancementPlayerWindow(null, null);
        var hub = new DeeperTabView();
        var host = new Window { Content = hub, Width = 1100, Height = 780 };
        try
        {
            editor.Show(); player.Show(); host.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Same(Theme("DeeperRoundIconToggleButton"), editor.FindControl<ToggleButton>("BtnCreatorLockToggle")!.Theme);
            // Named buttons, not Contains-any: one stray themed button must not hide a lost implicit style.
            Assert.Same(Theme("DeeperRoundButton"), editor.FindControl<Button>("BtnEditorHelp")!.Theme);
            Assert.Same(Theme("DeeperRoundButton"), player.FindControl<Button>("BtnOpenInEditor")!.Theme);
            Assert.Same(Theme("DeeperRoundButton"), player.FindControl<Button>("BtnPickAudio")!.Theme);
            Assert.Same(Theme("DeeperRoundButton"), hub.FindControl<Button>("BtnDeeperOpenPlayer")!.Theme);
        }
        finally { editor.Close(); player.Close(); host.Close(); CoreSettings.Current.MotionLevel = oldMotion; }
        await Task.CompletedTask;
    });
}
