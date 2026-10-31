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
}
