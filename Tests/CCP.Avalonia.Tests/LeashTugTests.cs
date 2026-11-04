using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LeashTugTests
{
    private static void Run(Action<Window, Window> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var panel = new Window { Content = new Border(), Width = 200, Height = 200 };
        var launcher = new Window { Content = new Border(), Width = 200, Height = 200 };
        try { launcher.Show(); Dispatcher.UIThread.RunJobs(); body(panel, launcher); }
        finally { LeashHead.Stop(); panel.Close(); launcher.Close(); }
    });

    private static LeashEvent Event(string id) => new(id, LeashEventKind.Tug,
        new LeashPerson("u_holder", "Holder", null), DateTimeOffset.UtcNow);

    [Fact]
    public void LauncherTugReportsSeenOnlyWhenMotionActuallyRuns() => Run((panel, launcher) =>
    {
        var reports = new List<string>();
        var shown = new List<string>();
        var service = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me", reportSeen: reports.Add);
        var notices = new LeashEventNotices(e => shown.Add(e.Id), service.NoteShown);
        var root = (Control)launcher.Content!;
        var prior = root.RenderTransform;
        LeashHead.PresentEvent(notices, Event("1111111111111111"), panel, launcher, MotionLevel.Full);
        Assert.Equal(new[] { "1111111111111111" }, reports);
        Assert.Empty(shown);
        Assert.IsType<TransformGroup>(root.RenderTransform);
        LeashHead.PresentEvent(notices, Event("1111111111111111"), panel, launcher, MotionLevel.Full);
        Assert.Single(reports);
        LeashHead.PresentEvent(notices, Event("2222222222222222"), panel, launcher, MotionLevel.Off);
        Assert.Single(reports);
        notices.Flush(false);
        Assert.Empty(shown);
        panel.Show(); Dispatcher.UIThread.RunJobs();
        notices.Flush(true);
        Assert.Equal(new[] { "2222222222222222" }, shown);
        Assert.Equal(new[] { "1111111111111111", "2222222222222222" }, reports);
        LeashHead.Stop();
        Assert.Same(prior, root.RenderTransform);
    });

    [Fact]
    public void TugUsesVisibleSurfaceAndReducedAmplitudeAndRestoresTransform() => Run((panel, launcher) =>
    {
        var tug = new LeashTug();
        var root = (Control)launcher.Content!;
        var prior = root.RenderTransform;
        try
        {
            Assert.False(tug.Play(panel, launcher, MotionLevel.Off));
            Assert.Same(prior, root.RenderTransform);
            Assert.True(tug.Play(panel, launcher, MotionLevel.Reduced));
            tug.Step(156);
            var group = Assert.IsType<TransformGroup>(root.RenderTransform);
            Assert.Equal(-4.8, group.Children.OfType<TranslateTransform>().Single().X, 3);
            Assert.Equal(-0.88, group.Children.OfType<RotateTransform>().Single().Angle, 3);
            Assert.True(tug.Play(panel, launcher, MotionLevel.Full));
            tug.Step(156);
            group = Assert.IsType<TransformGroup>(root.RenderTransform);
            Assert.Equal(-9.6, group.Children.OfType<TranslateTransform>().Single().X, 3);
            tug.Step(416);
            // 0.8 x 5 x (1 - 1.0765): the WPF BezierEase overshoots rest; a linear return would sit at +2.
            Assert.Equal(-0.306, group.Children.OfType<TranslateTransform>().Single().X, 3);
            Assert.Equal(0.44, group.Children.OfType<RotateTransform>().Single().Angle, 3);
            tug.Step(520);
            Assert.Same(prior, root.RenderTransform);
            launcher.Hide();
            Assert.False(tug.Play(panel, launcher, MotionLevel.Full));
        }
        finally { tug.Stop(); }
    });
}
