using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SectionDepthShellTests
{
    [Theory]
    [InlineData("NavRailShadow", "DepthRailShadow")]
    [InlineData("HudBandWellTop", "DepthWellTop")]
    [InlineData("XPGrooveTop", "DepthWellTop")]
    [InlineData("XPGrooveLeft", "DepthWellLeft")]
    public async Task HomeStartsWithTheNeutralDepthBrush(string name, string resource)
    {
        await InShell(w =>
        {
            Assert.Equal("settings", w.CurrentTab);
            Assert.Same(Application.Current!.FindResource(resource), w.Named<Border>(name)!.Background);
        });
    }

    [Fact]
    public async Task NavigationRetintsTheRailShadow()
    {
        await InShell(w =>
        {
            var shadow = w.Named<Border>("NavRailShadow")!;
            Assert.Equal(6, shadow.Width);
            Assert.Equal(HorizontalAlignment.Left, shadow.HorizontalAlignment);
            Assert.Equal((1, 5, 1, 59), (Grid.GetRow(shadow), Grid.GetRowSpan(shadow), Grid.GetColumn(shadow), shadow.ZIndex));
            Assert.False(shadow.IsHitTestVisible);
            CheckOnBothSections(w, shadow, 0xBF / 255.0, horizontal: true);
        });
    }

    [Fact]
    public async Task NavigationRetintsTheHudAndEmptyXpGroove()
    {
        await InShell(w =>
        {
            var top = w.Named<Border>("HudBandWellTop")!;
            var grooveTop = w.Named<Border>("XPGrooveTop")!;
            var grooveLeft = w.Named<Border>("XPGrooveLeft")!;
            var foot = w.Named<Border>("XPGrooveFoot")!;
            var track = w.Named<Border>("XPBarTrack")!;
            var fill = w.Named<Border>("XPBar")!;
            Assert.Equal(9, top.Height);
            Assert.Equal(10, track.Height);
            Assert.Equal(new CornerRadius(5), track.CornerRadius);
            Assert.Equal(new CornerRadius(5), fill.CornerRadius);
            Assert.Equal(Color.Parse("#FF120D20"), Assert.IsAssignableFrom<ISolidColorBrush>(track.Background).Color);
            Assert.Equal(Color.Parse("#12FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(foot.Background).Color);
            Assert.Equal(5, grooveTop.Height);
            Assert.Equal(6, grooveLeft.Width);
            Assert.Equal(1, foot.Height);
            Assert.False(grooveTop.IsHitTestVisible || grooveLeft.IsHitTestVisible || foot.IsHitTestVisible);
            var children = Assert.IsType<Grid>(track.Child).Children;
            Assert.True(children.IndexOf(grooveTop) < children.IndexOf(fill));
            Assert.True(children.IndexOf(grooveLeft) < children.IndexOf(fill));
            Assert.True(children.IndexOf(foot) < children.IndexOf(fill));
            CheckOnBothSections(w, top, DepthLaw.WellTopAlpha, horizontal: false);
            CheckOnBothSections(w, grooveTop, DepthLaw.WellTopAlpha, horizontal: false);
            CheckOnBothSections(w, grooveLeft, DepthLaw.WellLeftAlpha, horizontal: true);
        });
    }

    private static async Task InShell(System.Action<MainShellWindow> check)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
            {
                // Match QuestsDepthTests' bundled fallback for Windows-only font names on Linux.
                var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
                var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
                mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
                fonts.FontFamilyMappings = mappings;
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            }
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                check(w);
            }
            finally { w?.Close(); }
            return Task.CompletedTask;
        });
    }

    private static void CheckOnBothSections(MainShellWindow w, Border target, double alpha, bool horizontal)
    {
        Color? previous = null;
        foreach (var tab in new[] { "presets", "play" })
        {
            w.ShowTab(tab);
            Dispatcher.UIThread.RunJobs();
            var hue = NavStripRules.Accent(NavSections.SectionForTab(tab));
            var ink = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), alpha));
            var brush = Assert.IsType<LinearGradientBrush>(target.Background);
            Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), brush.StartPoint);
            Assert.Equal(new RelativePoint(horizontal ? 1 : 0, horizontal ? 0 : 1, RelativeUnit.Relative), brush.EndPoint);
            Assert.Equal(2, brush.GradientStops.Count);
            Assert.Equal((ink, 0), (brush.GradientStops[0].Color, brush.GradientStops[0].Offset));
            Assert.Equal((Color.FromArgb(0, ink.R, ink.G, ink.B), 1), (brush.GradientStops[1].Color, brush.GradientStops[1].Offset));
            if (previous.HasValue) Assert.NotEqual(previous.Value, ink);
            previous = ink;
        }
    }
}
