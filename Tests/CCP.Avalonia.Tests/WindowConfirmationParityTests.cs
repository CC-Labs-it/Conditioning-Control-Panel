using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WindowConfirmationParityTests
{
    private static Window Host(SectionTabStrip strip)
    {
        AmbientBeatLoopsTests.Setup();
        LocalizationManager.Instance.SetLanguage("en");
        var host = new Window { Content = strip, Width = 1400, Height = 180 };
        host.Show();
        strip.Show(NavSections.Studio, "presets");
        host.UpdateLayout();
        return host;
    }

    private static void Click(Control button)
    {
        var top = TopLevel.GetTopLevel(button)!;
        top.UpdateLayout();
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), top)!.Value;
        top.MouseMove(at);
        top.MouseDown(at, MouseButton.Left);
        top.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Open(SectionTabStrip strip)
    {
        Click(strip.PillFor("justdrop")!);
        Assert.Equal("justdrop", strip.ConfirmingKey);
        var card = Assert.IsType<Border>(strip.ConfirmationPopup!.Child);
        TopLevel.GetTopLevel(card)!.UpdateLayout();
        return card;
    }

    private static Button[] Buttons(Border card) => card.GetVisualDescendants().OfType<Button>().ToArray();
    private static Color Ink(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static uint Pixel(Bitmap frame, Point at)
    {
        var mem = Marshal.AllocHGlobal(4);
        try
        {
            frame.CopyPixels(new PixelRect((int)at.X, (int)at.Y, 1, 1), mem, 4, 4);
            return (uint)Marshal.ReadInt32(mem) & 0xFFFFFF;
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    [Fact]
    public Task RenderedCardAndRealButtonClicksMatchWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off };
        using var host = new ClosingWindow(Host(strip));
        int requests = 0;
        strip.TabRequested += _ => requests++;
        var card = Open(strip);
        var buttons = Buttons(card);
        Assert.Equal(2, buttons.Length);
        var hue = NavStripRules.Accent(NavSections.Studio);
        using (var frame = new RenderTargetBitmap(PixelSize.FromSize(card.Bounds.Size, 1), new Vector(96, 96)))
        {
            frame.Render(card);
            Point Face(Button b) => b.TranslatePoint(new Point(8, b.Bounds.Height / 2), card)!.Value;
            Assert.Equal((uint)(hue.R << 16 | hue.G << 8 | hue.B), Pixel(frame, Face(buttons[0])));
            Assert.NotEqual(Pixel(frame, Face(buttons[0])), Pixel(frame, Face(buttons[1])));
        }
        Assert.Equal(new Thickness(16, 12), card.Padding);
        Assert.Equal(new CornerRadius(14), card.CornerRadius);
        Assert.Equal(Color.Parse("#F51B1026"), Ink(card.Background));
        Assert.Equal(NavStripRules.WithAlpha(hue, 0.85), Ink(card.BorderBrush));
        var stack = Assert.IsType<StackPanel>(card.Child);
        var title = Assert.IsType<TextBlock>(stack.Children[0]);
        var body = Assert.IsType<TextBlock>(stack.Children[1]);
        Assert.Equal(14, title.FontSize);
        Assert.Equal(Colors.White, Ink(title.Foreground));
        Assert.Equal(12.5, body.FontSize);
        Assert.Equal(Color.Parse("#D9FFFFFF"), Ink(body.Foreground));
        Assert.Equal(3, body.Bounds.Y - title.Bounds.Bottom, 2);
        Assert.Equal(12, stack.Children[2].Bounds.Y - body.Bounds.Bottom, 2);
        foreach (var b in buttons)
        {
            var face = b.GetVisualDescendants().OfType<Border>().Single();
            Assert.Equal(new CornerRadius(11), face.CornerRadius);
            Assert.Equal(new Thickness(14, 5, 14, 6), face.Padding);
            var label = Assert.IsType<TextBlock>(b.Content);
            Assert.Equal(13, label.FontSize);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
        }
        Assert.Equal(NavStripRules.ActiveTextOn(hue), Ink(((TextBlock)buttons[0].Content!).Foreground));
        Assert.Equal(Color.Parse("#1FFFFFFF"), Ink(buttons[1].Background));
        Click(buttons[1]);
        Assert.Null(strip.ConfirmationPopup);
        Assert.Equal(0, requests);
        card = Open(strip);
        Click(Buttons(card)[0]);
        Assert.Null(strip.ConfirmationPopup);
        Assert.Equal(1, requests);
        Assert.Equal("presets", strip.ActivePillKey);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Off)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Full)]
    public Task FadeFollowsMotionAndStopsOnClose(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var strip = new SectionTabStrip { MotionOverride = level };
        using var host = new ClosingWindow(Host(strip));
        var old = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        try
        {
            var card = Open(strip);
            Assert.Equal(level == MotionLevel.Off ? 1 : 0, card.Opacity);
            time.Advance(TimeSpan.FromMilliseconds(75));
            BeatLoop.StepAll();
            Assert.Equal(level == MotionLevel.Off ? 1 : 0.5, card.Opacity, 3);
            time.Advance(TimeSpan.FromMilliseconds(75));
            BeatLoop.StepAll();
            Assert.Equal(1, card.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, card));
            Click(Buttons(card)[1]);
            card = Open(strip);
            strip.Rebuild(); // an interrupted fade must leave no clock or late opacity writer
            Assert.Null(strip.ConfirmationPopup);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, card));
        }
        finally { host.Window.Close(); BeatLoop.Time = old; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("dismiss")]
    [InlineData("rebuild")]
    [InlineData("remove")]
    public Task PopupDetachesAfterEveryClosePath(string path) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off };
        using var host = new ClosingWindow(Host(strip));
        var inheritance = typeof(AvaloniaObject).GetProperty("InheritanceParent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < 3; i++)
        {
            var card = Open(strip);
            var popup = strip.ConfirmationPopup!;
            Assert.Same(strip, popup.Parent);
            Assert.Same(strip, inheritance.GetValue(popup));
            switch (path)
            {
                case "confirm": Click(Buttons(card)[0]); break;
                case "cancel": Click(Buttons(card)[1]); break;
                case "dismiss":
                    host.Window.MouseDown(new Point(5, 175), MouseButton.Left);
                    host.Window.MouseUp(new Point(5, 175), MouseButton.Left);
                    break;
                case "rebuild": strip.Rebuild(); break;
                case "remove": host.Window.Content = null; break;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.Null(strip.ConfirmationPopup);
            Assert.Null(popup.Parent);
            Assert.Null(inheritance.GetValue(popup));
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, card));
            if (path == "remove") host.Window.Content = strip;
            host.Window.UpdateLayout();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WindowChoiceEffectsWaitForConfirmation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Full };
        using var host = new ClosingWindow(Host(strip));
        var old = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        int requests = 0;
        strip.TabRequested += _ => { Assert.Equal(1, strip.BurstsFired); requests++; };
        try
        {
            var card = Open(strip);
            Assert.Equal(0, strip.BurstsFired);
            time.Advance(TimeSpan.FromMilliseconds(150)); BeatLoop.StepAll();
            Click(Buttons(card)[1]);
            Assert.Equal(0, strip.BurstsFired);
            Assert.Equal(0, requests);
            card = Open(strip);
            Assert.Equal(0, strip.BurstsFired);
            time.Advance(TimeSpan.FromMilliseconds(150)); BeatLoop.StepAll();
            Click(Buttons(card)[0]);
            Assert.Equal(1, strip.BurstsFired);
            Assert.Equal(1, requests);
        }
        finally { host.Window.Close(); BeatLoop.Time = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnanchoredWindowStillRequestsItsTarget() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off };
        var tab = new NavTab("justdrop", "jd_door_title", NavTabKind.Window);
        int requests = 0;
        strip.TabRequested += requested => { Assert.Same(tab, requested); requests++; };
        typeof(SectionTabStrip).GetMethod("AskBeforeOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(strip, new object[] { tab });
        Assert.Equal(1, requests);
        Assert.Null(strip.ConfirmationPopup);
        return Task.CompletedTask;
    });

    private sealed class ClosingWindow(Window window) : IDisposable
    {
        public Window Window => window;
        public void Dispose() => window.Close();
    }
}
