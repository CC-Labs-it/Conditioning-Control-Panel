using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
    public void WakeMessagesFollowLanguageIncludingTheCalibrationDialog()
        => AvaloniaTestDispatcher.Run(() =>
        {
            var loc = LocalizationManager.Instance;
            var saved = loc.CurrentLanguage;
            loc.SetLanguage("en");
            var tab = new SheListeningTabView();
            var grid = Assert.IsAssignableFrom<Control>(tab.TxtSL_WakeEngineStatus.Parent!.Parent);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                var english = new[] { Loc.Get("she_wake_unavailable_tooltip"),
                    Loc.Get("she_wake_unavailable_status"), Loc.Get("she_wake_calibration_unavailable") };
                Assert.Contains("not available", english[0]);
                Assert.Equal("The optional wake engine is not available in this version. Using the built-in recognizer.", english[1]);
                foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
                {
                    loc.SetLanguage(code);
                    Dispatcher.UIThread.RunJobs();
                    var tooltip = Loc.Get("she_wake_unavailable_tooltip");
                    var status = Loc.Get("she_wake_unavailable_status");
                    var calibration = Loc.Get("she_wake_calibration_unavailable");
                    Assert.NotEqual("she_wake_unavailable_tooltip", tooltip);
                    Assert.NotEqual("she_wake_unavailable_status", status);
                    Assert.NotEqual("she_wake_calibration_unavailable", calibration);
                    if (code != "en")
                    {
                        Assert.NotEqual(english[0], tooltip);
                        Assert.NotEqual(english[1], status);
                        Assert.NotEqual(english[2], calibration);
                    }
                    Assert.Equal(tooltip, ToolTip.GetTip(grid));
                    Assert.Equal(status, tab.TxtSL_WakeEngineStatus.Text);
                    shell.SL_Calibrate_Click();
                    var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                    Assert.Equal(calibration, dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                    dialog.Close();
                }
            }
            finally
            {
                foreach (var dialog in shell.OwnedWindows.ToList()) dialog.Close();
                shell.RequestExit();
                loc.SetLanguage(saved);
            }
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
