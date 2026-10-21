using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SheListeningCommandRowsTests
{
    [Theory]
    [InlineData("Session", "resume", "“resume”", "“pause”")]
    [InlineData("Volume", "quieter", "“quieter”", "“louder”")]
    [InlineData("Volume", "mute", "“mute”", "“quieter”")]
    [InlineData("Video", "video_pause", "“pause the video”", "“resume the video”")]
    [InlineData("Video", "video_resume", "“resume the video”", "“pause the video”")]
    public void MixedRowsOnlyOfferRunnablePhrases(string row, string intent, string offered, string unavailable)
        => AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var tab = new SheListeningTabView();
            var head = tab.GetLogicalDescendants().OfType<IconLabel>().Single(h => h.Text == row);
            var panel = Assert.IsAssignableFrom<Panel>(head.Parent);
            var phrases = Assert.IsType<TextBlock>(panel.Children[panel.Children.IndexOf(head) + 1]);

            tab.ShowOnlyVoiceCommands(new HashSet<string> { intent });
            Assert.True(head.IsVisible);
            Assert.True(phrases.IsVisible);
            Assert.Contains(offered, PhraseText(phrases));
            Assert.DoesNotContain(unavailable, PhraseText(phrases));

            // Repaints must reconstruct the authored phrases after hiding every intent.
            tab.ShowOnlyVoiceCommands(new HashSet<string>());
            Assert.False(head.IsVisible);
            tab.ShowOnlyVoiceCommands(new HashSet<string> { intent });
            Assert.True(head.IsVisible);
            Assert.Contains(offered, PhraseText(phrases));
            Assert.DoesNotContain(unavailable, PhraseText(phrases));
        });

    [Fact]
    public void NarrowSettingsHideSideArtUntilTheHysteresisBoundary()
        => AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var tab = new SheListeningTabView();
            var grid = tab.FindControl<Grid>("SL_SettingsColumns");
            Assert.NotNull(grid);
            var art = tab.FindControl<Border>("SheListeningSideArt")!;
            void Resize(double width)
            {
                grid.Measure(new Size(width, 2000));
                grid.Arrange(new Rect(0, 0, width, 2000));
            }
            Resize(1100);
            Assert.True(art.IsVisible);
            Assert.Equal(780, grid.ColumnDefinitions[0].MaxWidth);
            Resize(999);
            Assert.False(art.IsVisible);
            Assert.Equal(0, grid.ColumnDefinitions[1].Width.Value);
            Assert.Equal(double.PositiveInfinity, grid.ColumnDefinitions[0].MaxWidth);
            Resize(1047);
            Assert.False(art.IsVisible);
            Resize(1048);
            Assert.True(art.IsVisible);
            Assert.Equal(new GridLength(2, GridUnitType.Star), grid.ColumnDefinitions[1].Width);
            Assert.Equal(780, grid.ColumnDefinitions[0].MaxWidth);
        });

    private static string PhraseText(TextBlock block) => block.Inlines?.Count > 0
        ? string.Concat(block.Inlines.OfType<Run>().Select(r => r.Text)) : block.Text ?? "";

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
