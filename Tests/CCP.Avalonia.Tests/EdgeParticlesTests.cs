using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;
namespace CCP.Avalonia.Tests;
public class EdgeParticlesTests
{
    [Fact]
    public async Task StripsFollowGateHueAndSimulation() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        var edge = new EdgeParticles { MotionOverride = MotionLevel.Full, TierAllowsParticlesOverride = true };
        edge.Mount(Colors.Pink);
        Assert.False(edge.IsHitTestVisible);
        Assert.Equal(4, edge.Strips.Count);
        Assert.Equal(new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left }, edge.Strips.Select(s => s.EdgeSide));
        var first = edge.Strips[0];
        Assert.Equal(30, first.Height);
        Assert.Equal(30, edge.Strips[1].Width);
        first.PrimeEdgeForTests(60);
        for (int i = 0; i < 8; i++) first.StepEdge(.7f);
        Assert.Equal(6, first.EdgeMoteCount);
        var before = first.EdgeMotesForTests();
        first.StepEdge(.05f);
        foreach (var m in first.EdgeMotesForTests())
        {
            Assert.InRange(m.Depth, .15f, .85f);
            var prior = before.Where(b => b.Depth == m.Depth).ToArray();
            if (prior.Length == 1) Assert.InRange(m.Along - prior[0].Along, .000899f, .001501f);
        }
        edge.Mount(Colors.Blue);
        Assert.Same(first, edge.Strips[0]);
        Assert.All(edge.Strips, s => Assert.Equal(Colors.Blue, s.Tint));
        foreach (var level in new[] { MotionLevel.Reduced, MotionLevel.Off })
        {
            edge.MotionOverride = level; edge.Refresh(); Assert.Empty(edge.Children);
            edge.MotionOverride = MotionLevel.Full; edge.Refresh(); Assert.Equal(4, edge.Strips.Count);
        }
        edge.TierAllowsParticlesOverride = false; edge.Refresh(); Assert.Empty(edge.Children);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task ShellMountsRetintsAndRefreshesEdge() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var w = new MainShellWindow();
        try
        {
            w.Show();
            var edge = w.Named<EdgeParticles>("SectionEdgeParticles")!;
            edge.TierAllowsParticlesOverride = true; edge.Refresh();
            Assert.Equal(4, edge.Strips.Count);
            w.ShowTab("presets");
            Assert.Equal(NavStripRules.Accent(NavSections.SectionForTab("presets")), edge.Hue);
            CoreSettings.Current.MotionLevel = MotionLevel.Off; w.OnMotionChrome();
            Assert.Empty(edge.Strips);
            CoreSettings.Current.MotionLevel = MotionLevel.Full; w.OnMotionChrome();
            Assert.Equal(4, edge.Strips.Count);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
}
