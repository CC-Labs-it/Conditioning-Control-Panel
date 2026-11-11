using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionPointerTests
{
    [Fact]
    public void TracksMotionHoverAndPressAndDetaches() => AvaloniaTestDispatcher.Run(() =>
    {
        var button = new Button { Width = 100, Height = 80, Content = "Start" };
        var window = new Window { Width = 200, Height = 150, Content = button };
        var pointer = new PossessionPointer();
        var hovers = 0;
        var presses = 0;
        pointer.HoverChanged += _ => hovers++;
        pointer.Pressed += _ => presses++;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            pointer.Attach(window);
            var now = DateTime.Now;
            pointer.Move(new Point(10, 10), button, now);
            pointer.Move(new Point(20, 10), button, now.AddSeconds(0.1));
            Assert.Equal(new Point(20, 10), pointer.Position);
            Assert.Equal(35, pointer.Velocity.X, 5);
            Assert.Same(button, pointer.Hovered);
            Assert.Equal(1, hovers);
            pointer.Press(new Point(20, 10), button, now);
            Assert.Same(button, pointer.LastClicked);
            Assert.Equal(now, pointer.LastClickAt);
            Assert.Equal(1, presses);
            pointer.Detach();
            Assert.Null(pointer.Hovered);
            Assert.Null(pointer.LastClicked);
            Assert.Equal(default, pointer.Velocity);
            window.MouseDown(new Point(20, 20), MouseButton.Left);
            Assert.Equal(1, presses);
        }
        finally { pointer.Detach(); window.Close(); }
    });

    [Fact]
    public void AttachedWindowReceivesInputUntilDetached() => AvaloniaTestDispatcher.Run(() =>
    {
        var button = new Button { Width = 100, Height = 80, Content = "Start" };
        var window = new Window { Width = 200, Height = 150, Content = button };
        var pointer = new PossessionPointer();
        var presses = 0;
        pointer.Pressed += _ => presses++;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var at = button.TranslatePoint(new Point(20, 20), window)!.Value;
            pointer.Attach(window);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Same(button, pointer.Hovered);
            Assert.Same(button, pointer.LastClicked);
            Assert.Equal(1, presses);
            pointer.Detach();
            window.MouseDown(at, MouseButton.Left);
            Assert.Equal(1, presses);
        }
        finally { pointer.Detach(); window.Close(); }
    });

    [Fact]
    public void ExcludedControlIsNotInteresting() => AvaloniaTestDispatcher.Run(() =>
    {
        var button = new Button();
        PossessionTree.SetExclude(button, true);
        Assert.Null(PossessionPointer.Interesting(button));
    });
}
