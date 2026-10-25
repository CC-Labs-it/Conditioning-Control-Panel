using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SectionStripWindowTests
{
    private static void SetUp()
    {
        if (Application.Current != null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Answer(SectionTabStrip strip, bool yes) =>
        strip.ConfirmationCard!.GetLogicalDescendants().OfType<Button>()
            .Single(b => b.Name == (yes ? "NavOpenWindowYes" : "NavOpenWindowNo"));

    [Theory]
    [InlineData(MotionLevel.Off)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Full)]
    public Task WindowPillAsksAndOnlyYesRequestsTheWindow(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetUp();
        var strip = new SectionTabStrip { MotionOverride = level };
        var host = new Window { Content = strip, Width = 1000, Height = 150 };
        var requests = 0;
        strip.TabRequested += tab => { Assert.Equal("justdrop", tab.Key); requests++; };
        try
        {
            host.Show();
            strip.Show(NavSections.Studio, "presets");
            Click(strip.PillFor("justdrop")!);
            Assert.Equal("justdrop", strip.ConfirmingKey);
            Assert.Equal("presets", strip.ActivePillKey);
            Assert.Equal(0, requests);
            if (level == MotionLevel.Off) Assert.Null(strip.ConfirmationCard!.Transitions);
            else Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<global::Avalonia.Animation.DoubleTransition>(strip.ConfirmationCard!.Transitions!.Single()).Duration);
            var text = strip.ConfirmationCard!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text);
            Assert.Contains(Loc.Get("nav_open_window_body"), text);
            Click(Answer(strip, false));
            Assert.Null(strip.ConfirmingKey);
            Assert.Equal(0, requests);
            Click(strip.PillFor("justdrop")!);
            Click(Answer(strip, true));
            Assert.Null(strip.ConfirmingKey);
            Assert.Equal(1, requests);
            Assert.Equal("presets", strip.ActivePillKey);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ConfirmationClosesOnEscapeRebuildSectionChangeDetachAndPanic() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetUp();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("presets");
            var strip = shell.PageStrip!;
            Assert.Contains("justdrop", strip.PillKeys);
            Click(strip.PillFor("justdrop")!);
            strip.ConfirmationCard!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Null(strip.ConfirmingKey);
            Click(strip.PillFor("justdrop")!);
            strip.Rebuild();
            Assert.Null(strip.ConfirmingKey);
            // Arrows focus a window without opening; Enter asks, as WPF OnStripKeyDown.
            strip.PillFor("haptics")!.Focus();
            strip.PillFor("haptics")!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Assert.Null(strip.ConfirmingKey);
            strip.PillFor("justdrop")!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("justdrop", strip.ConfirmingKey);
            Assert.Equal("presets", shell.CurrentTab);
            PanicSurfaces.All.Single(s => s.Id == "nav-window-confirm").Stop(shell);
            Assert.Null(strip.ConfirmingKey);
            Click(strip.PillFor("justdrop")!);
            shell.ShowTab("play");
            Assert.Null(strip.ConfirmingKey);
            shell.ShowTab("presets");
            Click(strip.PillFor("justdrop")!);
            shell.Close();
            Assert.Null(strip.ConfirmingKey);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SettingsBreadcrumbRereadsLabelAfterLanguageChange() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetUp();
        var loc = LocalizationManager.Instance;
        var before = loc.CurrentLanguage;
        var strip = new SectionTabStrip { SettingsPageLabel = () => Loc.Get("set2_section_general") };
        var host = new Window { Content = strip };
        try
        {
            host.Show();
            strip.Show(NavSections.Settings, "appsettings", "stale label");
            loc.SetLanguage(before == "de" ? "en" : "de");
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("stale label", strip.CrumbText);
            Assert.Contains(Loc.Get("set2_section_general"), strip.CrumbText);
        }
        finally { loc.SetLanguage(before); host.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public Task MovedNoteHidesAfterTheFadeTimer(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetUp();
        var strip = new SectionTabStrip { MotionOverride = level };
        var host = new Window { Content = strip };
        try
        {
            host.Show();
            strip.Show(NavSections.Studio, "presets");
            strip.ShowMovedNote("Moved");
            strip.ExpireMovedNote();
            Assert.True(strip.MovedNoteShown);
            await Task.Delay(SectionTabStrip.MovedNoteFade + TimeSpan.FromMilliseconds(150));
            Dispatcher.UIThread.RunJobs();
            Assert.False(strip.MovedNoteShown);
        }
        finally { host.Close(); }
    });
}
