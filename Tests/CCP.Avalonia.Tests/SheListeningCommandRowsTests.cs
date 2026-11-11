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
    public void WakeStatusDoesNotPromiseUnsupportedModel()
        => AvaloniaTestDispatcher.Run(() =>
        {
            var tab = new SheListeningTabView();
            Assert.Equal("The optional wake engine is not available in this version. Using the built-in recognizer.",
                tab.TxtSL_WakeEngineStatus.Text);
            var tooltip = ToolTip.GetTip(tab.TxtSL_WakeEngineStatus.Parent!.Parent as Control);
            Assert.Contains("not available", Assert.IsType<string>(tooltip));
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
