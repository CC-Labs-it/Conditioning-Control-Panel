using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeashConfirmMotionTests
{
    // WPF LeashCutConfirmWindow.cs:64-77, FriendsLook.cs:131-134, MotionFx.cs:75-103.
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void ConfirmPills_HoverPressReleaseAndLeave_MatchWpf(MotionLevel level) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = level;
        var button = LeashCutConfirmWindow.Pill("Yes", Brushes.Red, Brushes.Black, Brushes.Red, Brushes.Pink, "test");
        var scale = Assert.IsType<ScaleTransform>(button.RenderTransform);
        Assert.Equal(RelativePoint.Center, button.RenderTransformOrigin);
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var root = new Window();
        void Hover(bool on) => button.RaiseEvent(new PointerEventArgs(
            on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent,
            button, pointer, root, default, 0, PointerPointProperties.None, KeyModifiers.None));
        void Check(double value, int ms)
        {
            // Inspect target values, independent of the animation clock's current interpolation.
            Assert.Equal(value, scale.GetBaseValue(ScaleTransform.ScaleXProperty).GetValueOrDefault());
            Assert.Equal(value, scale.GetBaseValue(ScaleTransform.ScaleYProperty).GetValueOrDefault());
            if (level == MotionLevel.Off) Assert.Null(scale.Transitions);
            else
            {
                Assert.Equal(2, scale.Transitions!.Count);
                foreach (var transition in scale.Transitions)
                {
                    var t = Assert.IsType<DoubleTransition>(transition);
                    Assert.Equal(TimeSpan.FromMilliseconds(ms), t.Duration);
                    Assert.IsType<QuadraticEaseOut>(t.Easing);
                }
            }
        }
        Hover(true);
        Check(1.02, 150);
        button.RaiseEvent(new PointerPressedEventArgs(button, pointer, root, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Check(0.97, 80);
        button.RaiseEvent(new PointerReleasedEventArgs(button, pointer, root, default, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Check(1, 80);
        Hover(true);
        Check(1.02, 150);
        Hover(false);
        Check(1, 150);
        button.IsEnabled = false;
        Hover(true);
        Check(1, 150);
        root.Close();
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public void InterruptedMotion_StartsAtVisibleScale_WithoutSnapping(MotionLevel level) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = level;
        var button = LeashCutConfirmWindow.Pill("Yes", Brushes.Red, Brushes.Black, Brushes.Red, Brushes.Pink, "test");
        button.Content = null; // Exercise the transform without requiring a particular installed font.
        var scale = Assert.IsType<ScaleTransform>(button.RenderTransform);
        var clock = new StepClock();
        scale.SetValue(StepClock.Property, clock.Instance);
        var root = new Window { Content = button, Width = 200, Height = 100 };
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        void Check(double value)
        {
            Assert.Equal(value, scale.ScaleX, 6);
            Assert.Equal(value, scale.ScaleY, 6);
        }
        void Hover(bool on) => button.RaiseEvent(new PointerEventArgs(
            on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent,
            button, pointer, root, default, 0, PointerPointProperties.None, KeyModifiers.None));
        root.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            Hover(true);
            clock.Step(0); clock.Step(60);
            var hover = 1 + 0.02 * 0.64; // Quadratic ease out at 60 / 150 ms.
            Check(hover);
            var transitions = scale.Transitions;
            button.RaiseEvent(new PointerPressedEventArgs(button, pointer, root, default, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            Check(hover);
            Assert.Same(transitions, scale.Transitions);
            clock.Step(60); clock.Step(100);
            var pressed = hover + (0.97 - hover) * 0.75; // 40 / 80 ms.
            Check(pressed);
            button.RaiseEvent(new PointerReleasedEventArgs(button, pointer, root, default, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Check(pressed);
            clock.Step(100); clock.Step(140);
            var released = pressed + (1 - pressed) * 0.75;
            Check(released);
            Hover(true); // A new hover interrupts release and changes back to 150 ms.
            Check(released);
            clock.Step(140); clock.Step(200);
            var hoveredAgain = released + (1.02 - released) * 0.64;
            Check(hoveredAgain);
            Hover(false);
            Check(hoveredAgain);
            clock.Step(200); clock.Step(275);
            Check(hoveredAgain + (1 - hoveredAgain) * 0.75);
            clock.Step(350);
            Check(1);
        }
        finally { root.Close(); }
    });

    // Avalonia 12's clock is internal. Use the same hand-stepped ClockBase approach as
    // SharedDeeperThemeTests:137, without wall-clock sleeps or replacing transition behavior.
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

}
