using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF MainWindow.SectionChrome.cs:68 TryRedirectMovedTab: the old "together" key lands on
/// Social > Lobby and, the first three times across the app's life, the strip says "Moved".
/// </summary>
public sealed class SectionStripMovedNoteTests
{
    private static void SetUp()
    {
        if (Application.Current is not null) return;
        // The same bare builder the sibling shell tests use (SectionStripChoosePressTests).
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    [Fact]
    public async Task OldTogetherKeyLandsOnLobbyAndSaysMovedUntilTheLimit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var s = CoreSettings.Current;
            var hitsBefore = s.NavMovedToastHits;
            var w = new MainShellWindow();
            try
            {
                s.NavMovedToastHits = MainShellWindow.NavMovedNoteLimit - 1;
                w.Show();
                w.ShowTab("presets");
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                strip.MotionOverride = MotionLevel.Off;
                Dispatcher.UIThread.RunJobs();
                Assert.False(strip.MovedNoteShown);

                w.ShowTab("together");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("availablesubjects", w.CurrentTab);
                Assert.Equal(MainShellWindow.NavMovedNoteLimit, s.NavMovedToastHits);
                Assert.True(strip.MovedNoteShown);
                Assert.Equal(1, strip.MovedNoteOpacity);
                var expected = string.Format(Loc.Get("nav_moved_toast"),
                    Loc.Get(NavSections.Find(NavSections.Social)!.LabelKey),
                    Loc.Get(NavStripTable.PageLabelKey("availablesubjects")!));
                Assert.Equal(expected, strip.MovedNoteTextForTests);
                var hue = NavStripRules.WithAlpha(NavStripRules.Accent(NavSections.Social), NavStripRules.NoteTextAlpha);
                Assert.Equal(hue, ((ISolidColorBrush)strip.MovedNoteBrush!).Color);

                // The hold ends: at Off the note goes at once (WPF :520).
                strip.ExpireMovedNote();
                Assert.False(strip.MovedNoteShown);

                // The limit is spent: still redirected, never said again.
                w.ShowTab("presets");
                w.ShowTab("together");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("availablesubjects", w.CurrentTab);
                Assert.Equal(MainShellWindow.NavMovedNoteLimit, s.NavMovedToastHits);
                Assert.False(strip.MovedNoteShown);
            }
            finally
            {
                s.NavMovedToastHits = hitsBefore;
                w.Close();
            }
            return Task.CompletedTask;
        });
    }
}
