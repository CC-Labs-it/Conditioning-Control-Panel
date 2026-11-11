using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class Sync14ReviewTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void WatchCreditsCompletionCloseAndStallOnlyOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        foreach (var (elapsed, position, close, expected) in new[] { (30.0, 30.0, false, 30.0), (12.0, 12.0, true, 12.0), (50.0, 8.0, true, 8.0) })
        {
            double now = 0;
            var credits = new List<double>();
            var w = new BubbleCountWindow("unused", BubbleCountScheduler.Difficulty.Easy, false, _ => { })
                { WatchNow = () => now, WatchCredit = credits.Add };
            w.MarkWatchPlaying(); w.NoteWatchPosition(position); now = elapsed;
            if (close) typeof(BubbleCountWindow).GetMethod("CloseAllWindows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, new object[] { false });
            else w.CreditWatchTime();
            w.CreditWatchTime();
            Assert.Equal(expected, Assert.Single(credits), 3);
        }
    });

    [Fact]
    public void WatchHooksCoverBothEnginesEndAndPanic()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "CCP.Avalonia", "Views", "Windows", "BubbleCountWindow.axaml.cs"));
        Assert.Contains("player.Playing += (_, _) => MarkWatchPlaying()", source);
        Assert.Contains("player.TimeChanged += (_, a) => NoteWatchPosition", source);
        Assert.Matches(@"case ""playing"":\s*MarkWatchPlaying\(\)", source);
        Assert.Contains("if (_isPrimary) CreditWatchTime();", source);
        Assert.Contains("primary?.CreditWatchTime();", source);
        Assert.DoesNotContain("TrackVideoWatched(_videoDurationSeconds)", source);
    }

    [Fact]
    public void PaletteFindsTitleBarAndScopesTabHighlights() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            var bug = SettingsPaletteIndex.All.Single(e => e.Id == "chrome.bugreport");
            var bugButton = shell.FindControl<Button>("BtnTitleBarBugReport")!;
            Assert.Same(bugButton, SettingsPaletteWindow.FindHighlight(shell, bug));
            SettingsPaletteWindow.Navigate(shell, bug); Dispatcher.UIThread.RunJobs();
            Assert.IsType<DropShadowEffect>(bugButton.Effect);
            var duplicate = new Border { Name = "BtnActivateLockdown" };
            shell.FindControl<Grid>("RootGrid")!.Children.Insert(0, duplicate);
            var entry = new SettingsPaletteEntry { TabKey = "lockdown", ElementNames = new[] { duplicate.Name } };
            shell.ShowTab("lockdown"); shell.CompleteTabTransition(); Dispatcher.UIThread.RunJobs(); shell.UpdateLayout();
            var actual = shell.TabRoot("lockdown")!.FindControl<Button>(duplicate.Name);
            Assert.NotNull(actual);
            Assert.Same(actual, SettingsPaletteWindow.FindHighlight(shell, entry));
            Assert.NotSame(duplicate, SettingsPaletteWindow.FindHighlight(shell, entry));
            SettingsPaletteWindow.Navigate(shell, entry); Dispatcher.UIThread.RunJobs();
            Assert.IsType<DropShadowEffect>(actual.Effect);
            Assert.Null(duplicate.Effect);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public void PlainCardsDoNotLiftAndActiveHoverResets() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var motion = settings.MotionLevel;
        settings.MotionLevel = MotionLevel.Off;
        var card = new DailyQuestCard();
        var window = new Window { Content = card, Width = 400, Height = 500 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var root = card.FindControl<Border>("Root")!;
            var scale = (ScaleTransform)root.RenderTransform!;
            var at = root.TranslatePoint(new Point(50, 50), window)!.Value;
            card.SetNavigable(true); window.MouseMove(at);
            Assert.True(scale.ScaleX > 1);
            card.SetNavigable(false);
            Assert.Equal(1, scale.ScaleX); Assert.Equal(default, root.BoxShadow);
            window.MouseMove(new Point(390, 490)); window.MouseMove(at);
            Assert.Equal(1, scale.ScaleX); Assert.Equal(default, root.BoxShadow);
            var bubbles = QuestDefinition.DailyQuests.Single(q => q.Id == "pop_parade_d");
            var plain = QuestDefinition.DailyQuests.First(q => QuestDestinations.NoHome.Contains(q.Category));
            Assert.Null(QuestsTabView.DoorCategory(null, null));
            var completed = new ActiveQuest(bubbles.Id) { IsCompleted = true };
            Assert.Null(QuestsTabView.DoorCategory(completed, bubbles));
            Assert.Null(QuestsTabView.DoorCategory(new ActiveQuest(plain.Id), plain));
            Assert.Equal(bubbles.Category, QuestsTabView.DoorCategory(new ActiveQuest(bubbles.Id), bubbles));
        }
        finally { window.Close(); settings.MotionLevel = motion; Dispatcher.UIThread.RunJobs(); }
    });
}
