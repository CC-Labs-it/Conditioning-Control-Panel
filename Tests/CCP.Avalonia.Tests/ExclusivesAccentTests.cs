using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesAccentTests
{
    [Fact]
    public Task RefreshRetintsDecorWhileGoldAndTierCommerceStayFixed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var resources = Application.Current!.Resources;
        var old = resources["FxGlowColor"];
        Window? host = null;
        try
        {
            resources["FxGlowColor"] = Colors.Red;
            var view = new ExclusivesTabView();
            host = new Window { Width = 1400, Height = 900, Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var rows = Rows(view);
            var tiered = rows.First(r => r.Tier > 0);
            var tierEdge = tiered.EdgeBrush;
            var gold = ((ISolidColorBrush)view.FindControl<Border>("SpotFreeToday")!.BorderBrush!).Color;
            var untiered = rows.First(r => r.Tier == 0 && !r.FreeToday);
            Assert.Equal(Color.FromArgb(0x4D, 255, 0, 251), ((ISolidColorBrush)untiered.EdgeBrush).Color);
            var badge = (LinearGradientBrush)view.FindControl<Border>("SpotBadge")!.Background!;
            Assert.Equal(Colors.Red, badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(255, 0, 251), badge.GradientStops[1].Color);

            // The same mounted view must repaint when its mod changes; constructing a second view
            // would miss a stale resource/brush retained on the spotlight.
            resources["FxGlowColor"] = Colors.Blue;
            view.RefreshVault();
            Dispatcher.UIThread.RunJobs();
            rows = Rows(view);
            Assert.Equal(Color.FromArgb(0x4D, 0, 251, 255),
                ((ISolidColorBrush)rows.First(r => r.Tier == 0 && !r.FreeToday).EdgeBrush).Color);
            Assert.Equal(Colors.Blue, badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(0, 251, 255), badge.GradientStops[1].Color);
            Assert.Contains(view.GetVisualDescendants().OfType<Border>()
                .Select(b => b.Background).OfType<LinearGradientBrush>(), brush =>
                brush.GradientStops.Count == 2 &&
                brush.GradientStops[0].Color == Color.FromArgb(0xE6, 0, 0, 255) &&
                brush.GradientStops[1].Color == Color.FromArgb(0xD9, 0, 251, 255));
            Assert.Equal(tierEdge, rows.First(r => r.Feature.Key == tiered.Feature.Key).EdgeBrush);
            Assert.Equal(gold, ((ISolidColorBrush)view.FindControl<Border>("SpotFreeToday")!.BorderBrush!).Color);
        }
        finally
        {
            host?.Close();
            if (old is null) resources.Remove("FxGlowColor");
            else resources["FxGlowColor"] = old;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static ExclusiveCardRow[] Rows(ExclusivesTabView view) =>
        view.FindControl<ItemsControl>("ExclusivesShelf")!.Items.OfType<ExclusiveCardRow>().ToArray();
}
