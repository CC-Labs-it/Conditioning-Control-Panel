using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDockParityTests
{
    [Fact]
    public Task CollapsedRailKeepsBothMutedStatesAt64Pixels() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dock = new EmiDock();
        var host = new Window { Width = 56, Height = 200, Content = dock };
        host.Show();
        try
        {
            foreach (bool muted in new[] { false, true })
            {
                dock.Refresh(false, muted);
                dock.Measure(new Size(56, double.PositiveInfinity));
                Assert.Equal(64, dock.DesiredSize.Height - dock.Margin.Top - dock.Margin.Bottom);
                var name = dock.FindControl<TextBlock>("TxtName")!;
                var pill = dock.FindControl<TextBlock>("TxtMuted")!;
                Assert.Equal(1, Grid.GetRow(name));
                Assert.Equal(2, Grid.GetRow(pill));
                Assert.Equal(TextWrapping.NoWrap, pill.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, pill.TextTrimming);
                Assert.Equal(muted, pill.IsVisible);
                Assert.Equal(10.5, name.FontSize);
                Assert.Equal(FontWeight.ExtraBold, name.FontWeight);
            }
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task FaceUsesInkGeometryAndLockedCanvasFit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var normal = EmiFace.Layout("0_0", false, false);
        var sideways = EmiFace.Layout(":)", false, false);
        var flat = EmiFace.Layout(":)", false, true);
        var small = EmiFace.Layout("...", true, false);
        Assert.NotNull(normal.Geo);
        Assert.True(normal.Ink.Width > 1 && normal.Ink.Height > 1);
        Assert.True(normal.FontSize > small.FontSize);
        Assert.True(Math.Abs(sideways.Place.M12) > 0.9);
        Assert.Equal(0, flat.Place.M12);
        var dock = new EmiDock();
        Assert.Equal("0_0", dock.FindControl<EmiFace>("MiniFace")!.Face);
        return Task.CompletedTask;
    });

    [Fact]
    public Task KnockBuildsWpfPulseTimelinesAndUnloadRestoresRestingRing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dock = new EmiDock();
        var host = new Window { Width = 100, Height = 100, Content = dock };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var glow = dock.FindControl<Border>("RingGlow")!;
            var ring = dock.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Ring")!;
            Assert.Null(ring.Effect);
            dock.StartKnock();
            // No clock stepping: Avalonia 12 keeps IClock internal (P08), so read the timelines.
            Assert.True(dock.IsKnocking);
            foreach (var anim in new[] { dock.KnockRing!, dock.KnockGlow! })
            {
                Assert.Equal(TimeSpan.FromSeconds(2), anim.Duration);
                Assert.Equal(new global::Avalonia.Animation.IterationCount(3), anim.IterationCount);
                Assert.Equal(new[] { 0.0, 0.18, 0.55, 1.0 }, anim.Children.Select(f => f.Cue.CueValue));
            }
            var peak = dock.KnockGlow!.Children[1].Setters.Cast<global::Avalonia.Styling.Setter>().Single();
            Assert.Equal(Visual.OpacityProperty, peak.Property);
            Assert.Equal(0.95, peak.Value);
            Assert.Equal(3.2, dock.KnockRing!.Children[1].Setters.Cast<global::Avalonia.Styling.Setter>().Single(x => x.Property == global::Avalonia.Controls.Shapes.Shape.StrokeThicknessProperty).Value);
            Assert.Equal(0.0, dock.KnockGlow.Children[2].Setters.Cast<global::Avalonia.Styling.Setter>().Single().Value);
            Assert.Equal("0_0", dock.FindControl<EmiFace>("MiniFace")!.Face);
            dock.Refresh(true);
            Assert.True(dock.IsKnocking); // arrival does not kill the discovery pulse
            host.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(dock.IsKnocking);
            Assert.Null(dock.KnockGlow);
            Assert.Equal(0, glow.Opacity);
            Assert.Equal(2, ring.StrokeThickness);
            Assert.Equal(Color.Parse("#FF69B4"), ((SolidColorBrush)ring.Stroke!).Color);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });
}
