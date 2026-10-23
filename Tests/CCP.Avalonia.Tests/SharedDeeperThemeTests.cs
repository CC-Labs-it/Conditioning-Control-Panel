using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using HudPlank = ConditioningControlPanel.Avalonia.Controls.HudPlank;
using HudPlankKind = ConditioningControlPanel.Avalonia.Controls.HudPlankKind;
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
        var ease = Assert.IsType<global::ConditioningControlPanel.Avalonia.Theme.BackEaseOutAmplitude>(releaseTween.Easing);
        Assert.Equal(0.3, ease.Amplitude);
        // WPF BackEase EaseOut, Amplitude 0.3: 1 - (x^3 - 0.3 x sin(pi x)), x = 1 - t.
        Assert.Equal(0.0, ease.Ease(0), 9);
        Assert.Equal(0.629068, ease.Ease(0.2), 5);
        Assert.Equal(1.025, ease.Ease(0.5), 9);
        Assert.Equal(1.0, ease.Ease(1), 9);
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
                var clock = new StepClock();
                border.SetValue(StepClock.Property, clock.Instance);
                double Scale() => border.RenderTransform?.Value.M11 ?? 1.0;   // settled release = no transform
                // Press: 80 ms linear 1 -> 0.97 (the 150 ms BackEase would sit at 0.9811 at 30 ms, not done at 80).
                window.MouseDown(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.Same(press, border.Transitions);
                clock.Step(0); clock.Step(40);
                Assert.Equal(0.985, Scale(), 4);
                clock.Step(80);
                Assert.Equal(0.97, Scale(), 4);
                // Release: 150 ms BackEase 0.3 0.97 -> 1 (the 80 ms linear tween would read 0.98125 at 30 ms).
                window.MouseUp(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.Same(release, border.Transitions);
                clock.Step(1000); clock.Step(1030);
                Assert.Equal(0.97 + 0.03 * 0.629068, Scale(), 4);
                clock.Step(1075);
                Assert.Equal(0.97 + 0.03 * 1.025, Scale(), 4);
                clock.Step(1150);
                Assert.Equal(1.0, Scale(), 4);
            }
            finally { window.Close(); }
        }
        await Task.CompletedTask;
    });

    /// <summary>Hand-stepped animation clock, so transitions advance only when the test says so. Avalonia 12 made
    /// IClock internal, and the headless render clock is real time, so this reaches Avalonia's own ClockBase by
    /// reflection; a rename there fails loudly here instead of silently passing.</summary>
    private sealed class StepClock
    {
        private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        private static readonly Type Base = typeof(Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", throwOnError: true)!;
        private static readonly System.Reflection.MethodInfo Pulse = Base.GetMethod("Pulse", Any | System.Reflection.BindingFlags.Instance)!;
        public static readonly AvaloniaProperty Property =
            (AvaloniaProperty)typeof(Animatable).GetField("ClockProperty", Any | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        public readonly object Instance = Activator.CreateInstance(Base, nonPublic: true)!;
        public void Step(double ms) => Pulse.Invoke(Instance, new object[] { TimeSpan.FromMilliseconds(ms) });
    }

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

    [Fact]
    public Task ToggleKnobEntersAndExitsOnWpfCurvesAndTheSparkFadesIn80ms() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var enter = Application.Current!.FindResource("ToggleKnobEnterTransitions");
        var exit = Application.Current!.FindResource("ToggleKnobExitTransitions");
        var toggle = new CheckBox { Theme = Theme("ToggleStyle") };
        var window = new Window { Content = toggle, Width = 200, Height = 100 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var thumb = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "thumb");
            var spark = toggle.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Name == "spark");
            var clock = new StepClock();
            thumb.SetValue(StepClock.Property, clock.Instance);
            spark.SetValue(StepClock.Property, clock.Instance);
            double X() => thumb.RenderTransform?.Value.M31 ?? 0;
            // ON: 340 ms BackEase(0.6). At t = 0.5 that is 1 - (0.125 - 0.6 * 0.5) = 1.175 of the 22 px slide
            // (Avalonia's amplitude-1 BackEaseOut would read 1.375).
            toggle.IsChecked = true; Dispatcher.UIThread.RunJobs();
            Assert.Same(enter, thumb.Transitions);
            clock.Step(0); clock.Step(170);
            Assert.Equal(22 * 1.175, X(), 3);
            clock.Step(340);
            Assert.Equal(22, X(), 3);
            double lit = spark.Opacity;   // the 450 ms decay, three quarters through
            Assert.InRange(lit, 0.1, 0.5);
            // OFF: 300 ms BackEase(0.35): at t = 0.5, 1 - (0.125 - 0.175) = 1.05 of the way back, i.e. -1.1 px.
            toggle.IsChecked = false; Dispatcher.UIThread.RunJobs();
            Assert.Same(exit, thumb.Transitions);
            clock.Step(1000); clock.Step(1040);
            // The spark fades over 80 ms (WPF ExitActions), it is not cut dead.
            Assert.InRange(spark.Opacity, 0.01, lit - 0.01);
            clock.Step(1080);
            Assert.Equal(0, spark.Opacity, 3);
            clock.Step(1150);
            Assert.Equal(-1.1, X(), 3);
            clock.Step(1300);
            Assert.Equal(0, X(), 3);
            // ON again: the flash wins over the fade (0.9 at 60 ms).
            toggle.IsChecked = true; Dispatcher.UIThread.RunJobs();
            clock.Step(2000); clock.Step(2060);
            Assert.InRange(spark.Opacity, 0.8, 0.95);
        }
        finally { window.Close(); }
        await Task.CompletedTask;
    });

    [Fact]
    public Task HudPlanksRaiseTheStartRowAndTravelOnHoverPressRelease() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldMotion = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        static double Base(AvaloniaObject o, StyledProperty<double> p) => o.GetBaseValue(p) is { HasValue: true } v ? v.Value : double.NaN;
        try
        {
            foreach (var (key, kind) in new[]
            {
                ("PinkButton", HudPlankKind.None), ("PinkButton", HudPlankKind.Plank), ("PinkButton", HudPlankKind.Chunky),
                ("SecondaryButton", HudPlankKind.None), ("SecondaryButton", HudPlankKind.Plank),
            })
            {
                var button = new Button { Theme = Theme(key), Content = "Test", Width = 120, Height = 40 };
                HudPlank.SetKind(button, kind);
                var window = new Window { Content = button, Width = 200, Height = 100 };
                window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    Control Part(string n) => button.GetVisualDescendants().OfType<Control>().Single(c => c.Name == n);
                    bool raised = kind != HudPlankKind.None;
                    Assert.Equal(raised, Part("DepthDrop").IsVisible);
                    Assert.Equal(raised, Part("DepthSheen").IsVisible);
                    Assert.Equal(raised, Part("DepthBevel").IsVisible);
                    if (key == "PinkButton") Assert.Equal(raised ? 0 : 1.5, ((Border)Part("border")).BorderThickness.Top);
                    bool chunky = kind == HudPlankKind.Chunky;
                    Assert.Same(Application.Current!.FindResource(chunky ? "DepthPlankSheen" : "DepthRaisedSheen"), ((Border)Part("DepthSheen")).Background);
                    Assert.Same(Application.Current!.FindResource(chunky ? "DepthPlankBevel" : "DepthRaisedBevel"), ((Border)Part("DepthBevel")).BorderBrush);
                    if (!raised) continue;

                    var drop = Part("DepthDrop");
                    double h = chunky ? 7 : 5;   // StartPx / RaisedPx + HoverLiftPx
                    Assert.Equal(h, drop.Height);
                    Assert.Equal(-h, drop.Margin.Bottom);
                    var slide = (TranslateTransform)Part("DepthFace").RenderTransform!;
                    var clock = new StepClock();
                    slide.SetValue(StepClock.Property, clock.Instance);
                    double Travel() => Base(slide, TranslateTransform.YProperty);
                    double Drop() => Base((ScaleTransform)drop.RenderTransform!, ScaleTransform.ScaleYProperty) * h;
                    // Rest: on the sheet, RaisedPx drop (StartPx for START).
                    Assert.Equal(0, Travel(), 3); Assert.Equal(chunky ? 5 : 3, Drop(), 3);
                    // Hover lifts 2 px and lengthens the drop by the same.
                    window.MouseMove(new Point(100, 50)); Dispatcher.UIThread.RunJobs();
                    Assert.Equal(-2, Travel(), 3); Assert.Equal(h, Drop(), 3);
                    // Press drops 2 px and the shadow goes; a plank SecondaryButton does not also squash.
                    window.MouseDown(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    Assert.Equal(2, Travel(), 3); Assert.Equal(0, Drop(), 3);
                    if (key == "SecondaryButton") Assert.Null(Part("border").RenderTransform);
                    clock.Step(0); clock.Step(500);
                    Assert.Equal(2, slide.Y, 3);
                    // Release springs back through 1 px past rest at 60% of 140 ms (WPF keyframes): 2 -> -3 -> -2.
                    window.MouseUp(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    Assert.Equal(-2, Travel(), 3);
                    clock.Step(1000); clock.Step(1084);
                    Assert.Equal(-3, slide.Y, 3);
                    clock.Step(1140);
                    Assert.Equal(-2, slide.Y, 3);
                    // A press that lands mid-hover-glide starts from where the face is, not the hover target.
                    window.MouseMove(new Point(5, 5)); Dispatcher.UIThread.RunJobs();
                    clock.Step(2000); clock.Step(2200);
                    Assert.Equal(0, slide.Y, 3);
                    window.MouseMove(new Point(100, 50)); Dispatcher.UIThread.RunJobs();
                    clock.Step(3000); clock.Step(3060);   // half of HoverMs, QuadraticEaseOut: 0 -> -1.5
                    Assert.Equal(-1.5, slide.Y, 2);
                    window.MouseDown(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    clock.Step(3061);
                    Assert.InRange(slide.Y, -1.6, -1.3);
                    clock.Step(3300);
                    Assert.Equal(2, slide.Y, 3);
                    window.MouseUp(new Point(100, 50), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    clock.Step(4000); clock.Step(4300);
                    Assert.Equal(-2, slide.Y, 3);
                    // Disabled: flat, no shadow.
                    button.IsEnabled = false; Dispatcher.UIThread.RunJobs();
                    Assert.Equal(0, Travel(), 3); Assert.Equal(0, Drop(), 3);
                }
                finally { window.Close(); }
            }

            // The real callers: WPF MainWindow.xaml:2243-2427 (Remember, START, the caret, Save, Exit).
            var oldProvider = CoreSettings.ServiceProvider;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            MainShellWindow? shell = null;
            try
            {
                shell = new MainShellWindow();
                shell.Show(); Dispatcher.UIThread.RunJobs();
                foreach (var (name, kind) in new[]
                {
                    ("BtnRemember", HudPlankKind.Plank), ("BtnStart", HudPlankKind.Chunky), ("BtnStartMenu", HudPlankKind.Plank),
                    ("BtnSaveAll", HudPlankKind.Plank), ("BtnExit", HudPlankKind.Plank),
                })
                {
                    var b = shell.Named<Button>(name)!;
                    Assert.Equal(kind, HudPlank.GetKind(b));
                    var drop = b.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "DepthDrop");
                    Assert.True(drop.IsVisible, name);
                    Assert.Equal(kind == HudPlankKind.Chunky ? 7 : 5, drop.Height);
                }
            }
            finally { shell?.Close(); CoreSettings.ServiceProvider = oldProvider; }
        }
        finally { CoreSettings.Current.MotionLevel = oldMotion; }
        await Task.CompletedTask;
    });
}
