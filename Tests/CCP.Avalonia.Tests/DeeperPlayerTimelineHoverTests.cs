using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayerTimelineHoverTests
{
    // WPF EnhancementPlayerWindow.xaml:520-543 and Mission3.cs:717-749.
    [Fact]
    public Task HoverShowsClampedTimeAndDoesNotInterruptScrubbing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var enhancement = new Enhancement { MediaType = MediaTypes.Video, Regions = new() { new Region { Start = 0, End = 120 } } };
        var w = new EnhancementPlayerWindow(enhancement, "test");
        try
        {
            var scripts = new List<string>();
            w.FindControl<WebHost>("VideoBrowser")!.ScriptInvoker = s => { scripts.Add(s); return Task.FromResult<string?>("ok"); };
            w.GetType().GetField("_videoNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(w, true);
            w.Show();
            w.UpdateLayout();
            var canvas = w.FindControl<Canvas>("MiniTimelineCanvas")!;
            var label = w.FindControl<Border>("MiniHoverLabel")!;
            var text = w.FindControl<TextBlock>("TxtMiniHoverTime")!;
            Assert.True(canvas.Bounds.Width > 100);
            Point At(double x) => canvas.TranslatePoint(new Point(x, canvas.Bounds.Height / 2), w)!.Value;

            w.MouseMove(At(canvas.Bounds.Width / 4));
            w.UpdateLayout();
            Assert.True(label.IsVisible);
            Assert.Equal("0:30", text.Text);
            Assert.DoesNotContain(scripts, s => s.Contains("l.currentTime="));

            w.MouseMove(At(canvas.Bounds.Width - 1));
            w.UpdateLayout();
            Assert.True(label.Margin.Left >= 0);
            Assert.True(label.Margin.Left + label.Bounds.Width <= canvas.Bounds.Width + 0.1);
            Assert.Equal("1:59", text.Text);

            w.MouseDown(At(canvas.Bounds.Width / 4), MouseButton.Left);
            w.MouseMove(At(canvas.Bounds.Width / 2));
            w.MouseUp(At(canvas.Bounds.Width / 2), MouseButton.Left);
            Assert.Contains(scripts, s => s.Contains("l.currentTime=60;"));
            Assert.Equal("1:00", text.Text);

            w.MouseMove(new Point(1, 1));
            Dispatcher.UIThread.RunJobs();
            Assert.False(label.IsVisible);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
}
