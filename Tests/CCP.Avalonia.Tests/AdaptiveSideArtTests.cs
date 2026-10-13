using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

    // Haptics and RemoteControl: their Avalonia ports dropped the art column (parity views-tab-haptics).
    private static readonly string[] NoArtColumn = { "HapticsTabView", "RemoteControlTabView" };

    /// <summary>Every WPF view with CollapseBelow has an Avalonia twin carrying the same threshold(s).</summary>
    [Fact]
    public void EveryWpfCollapseBelowThresholdIsCarriedByItsAvaloniaTwin()
    {
        var root = RepoRoot();
        var attr = new Regex(@"AdaptiveSideArt\.CollapseBelow=""(\d+)""");
        var comment = new Regex(@"<!--.*?-->", RegexOptions.Singleline);
        string Values(string file) => string.Join(",", attr.Matches(comment.Replace(File.ReadAllText(file), ""))
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(v => v));

        var avalonia = Directory.GetFiles(Path.Combine(root, "CCP.Avalonia", "Views"), "*.axaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f));
        var wpf = Directory.GetFiles(Path.Combine(root, "ConditioningControlPanel"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => Values(f).Length > 0).ToArray();

        Assert.Equal(23, wpf.Length);
        var checkedViews = 0;
        foreach (var w in wpf)
        {
            var name = Path.GetFileNameWithoutExtension(w);
            if (NoArtColumn.Contains(name)) continue;
            Assert.True(avalonia.TryGetValue(name, out var a), $"{name}: no Avalonia twin");
            Assert.Equal((name, Values(w)), (name, Values(a!)));
            checkedViews++;
        }
        Assert.Equal(21, checkedViews);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
