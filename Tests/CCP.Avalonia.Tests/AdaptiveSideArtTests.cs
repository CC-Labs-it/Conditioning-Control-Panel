using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Features/AdaptiveSideArt.cs: below CollapseBelow the last column goes to zero, its
/// art hides and the settings column's MaxWidth lifts; it comes back only past threshold + 48 (#1321).</summary>
public sealed class AdaptiveSideArtTests
{
    [Fact]
    public async Task ResizingAHostAcrossTheThresholdCollapsesAndRestoresTheArtColumn()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

            // A real ported view: the attached property is set from its XAML, same 700 as WPF.
            var view = new SubliminalFeatureControl();
            var host = new Window { Width = 1100, Height = 800, Content = view };
            try
            {
                host.Show();
                Dispatcher.UIThread.RunJobs();
                var art = view.FindControl<Border>("SideArt")!;
                var grid = art.GetVisualAncestors().OfType<Grid>().First(g => !double.IsNaN(AdaptiveSideArt.GetCollapseBelow(g)));
                Assert.Equal(700, AdaptiveSideArt.GetCollapseBelow(grid));
                var artCol = grid.ColumnDefinitions[^1];
                var settingsCol = grid.ColumnDefinitions[0];

                void Resize(double w) { host.Width = w; Dispatcher.UIThread.RunJobs(); }

                Assert.True(art.IsVisible);
                Assert.Equal(780, settingsCol.MaxWidth);

                Resize(600);
                Assert.True(grid.Bounds.Width < 700);
                Assert.False(art.IsVisible);
                Assert.Equal(0, artCol.Width.Value);
                Assert.True(double.IsPositiveInfinity(settingsCol.MaxWidth));

                // Hysteresis: just past the threshold but inside WidenMargin stays compact.
                Resize(720 + (host.Width - grid.Bounds.Width));
                Assert.InRange(grid.Bounds.Width, 700, 700 + AdaptiveSideArt.WidenMargin - 1);
                Assert.False(art.IsVisible);

                Resize(1100);
                Assert.True(art.IsVisible);
                Assert.Equal(new GridLength(2, GridUnitType.Star), artCol.Width);
                Assert.Equal(780, settingsCol.MaxWidth);
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }
}
