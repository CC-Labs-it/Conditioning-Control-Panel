using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Rows;
using ConditioningControlPanel.Emblems;
using Xunit;

namespace CCP.Avalonia.Tests;

public class RowValueTests
{
    [Fact]
    public Task NumberAndOptionalUnitFollowChanges() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var row = new RowValue { Text = "180", Unit = "an hour" };
        Assert.Equal("180", row.NumberPart.Text);
        Assert.Equal("an hour", row.UnitPart.Text);
        Assert.True(row.UnitPart.IsVisible);
        row.Text = "12";
        row.Unit = "";
        Assert.Equal("12", row.NumberPart.Text);
        Assert.False(row.UnitPart.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NumberTracksInheritedHueAndFallsBack() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var row = new RowValue();
        var parent = new StackPanel { Children = { row } };
        var host = new Window { Content = parent };
        try
        {
            RowHue.SetHue(parent, Colors.CornflowerBlue);
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.CornflowerBlue, row.EffectiveHue);
            var expected = EmblemPalette.ValueTint(RowHue.ToArgb(Colors.CornflowerBlue));
            Assert.Equal(RowHue.ToColor(expected), ((ISolidColorBrush)row.NumberPart.Foreground!).Color);
            RowHue.SetHue(parent, Colors.Gold);
            Assert.Equal(Colors.Gold, row.EffectiveHue);
            expected = EmblemPalette.ValueTint(RowHue.ToArgb(Colors.Gold));
            Assert.Equal(RowHue.ToColor(expected), ((ISolidColorBrush)row.NumberPart.Foreground!).Color);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });
}
