using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
            host.UpdateLayout();
            var bursts = strip.BurstsFired;
            Click(strip.PillFor("justdrop")!);
            Assert.Equal(bursts, strip.BurstsFired);
            Assert.Equal("justdrop", strip.ConfirmingKey);
            Assert.Equal("presets", strip.ActivePillKey);
            Assert.Equal(0, requests);
            if (level == MotionLevel.Off) Assert.Null(strip.ConfirmationCard!.Transitions);
            else Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<global::Avalonia.Animation.DoubleTransition>(strip.ConfirmationCard!.Transitions!.Single()).Duration);
            var text = strip.ConfirmationCard!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text);
            Assert.Contains(Loc.Get("nav_open_window_body"), text);
            // WPF ConfirmButton: own template whose Border carries the hue fill; text in a TextBlock (no access key).
            var yes = Answer(strip, true);
            Assert.IsType<TextBlock>(yes.Content);
            Assert.NotNull(yes.Template);
            yes.ApplyTemplate();
            var bd = Assert.IsType<Border>(Assert.Single(global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(yes)));
            Assert.Same(yes.Background, bd.Background);
            Assert.Equal("Hand", yes.Cursor!.ToString());
            yes.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, yes, new Pointer(1, PointerType.Mouse, true), yes, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.Equal(0.85, yes.Opacity);
            yes.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, yes, new Pointer(1, PointerType.Mouse, true), yes, default, 0, new PointerPointProperties(), KeyModifiers.None));
            Assert.Equal(1, yes.Opacity);
            Click(Answer(strip, false));
            Assert.Equal(bursts, strip.BurstsFired);
            Assert.Null(strip.ConfirmingKey);
            Assert.Equal(0, requests);
            Click(strip.PillFor("justdrop")!);
            var popup = (Popup)strip.ConfirmationCard!.Parent!;
            Assert.True(popup.IsLightDismissEnabled);
            popup.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(strip.ConfirmingKey);
            Click(strip.PillFor("justdrop")!);
            Click(Answer(strip, true));
            Assert.Equal(bursts + (level == MotionLevel.Off ? 0 : 1), strip.BurstsFired);
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
        var service = new global::ConditioningControlPanel.Services.SettingsService();
        global::ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var st = global::ConditioningControlPanel.CoreSettings.Current;
        (st.Welcomed, st.HasAcceptedAgeVerification, st.WhatMovedCardShown) = (true, true, 1);   // P52/P76: no intro takes focus
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("presets");
            Dispatcher.UIThread.RunJobs();
            var strip = shell.PageStrip!;
            Assert.Contains("justdrop", strip.PillKeys);
            Click(strip.PillFor("justdrop")!);
            strip.ConfirmationCard!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Null(strip.ConfirmingKey);
            Click(strip.PillFor("justdrop")!);
            strip.Rebuild();
            Assert.Null(strip.ConfirmingKey);
            // Arrows focus a window without opening; Enter asks, as WPF OnStripKeyDown.
            // (ramp, not haptics: haptics is a locked premium pill on a free account.)
            Dispatcher.UIThread.RunJobs();
            var ramp = strip.PillFor("ramp")!;
            Assert.True(ramp.Focus(), $"ramp pill takes focus (visible {ramp.IsEffectivelyVisible}, enabled {ramp.IsEffectivelyEnabled}, shell enabled {shell.IsEnabled}, focused {shell.FocusManager?.GetFocusedElement()})");
            strip.PillFor("ramp")!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
            Assert.Null(strip.ConfirmingKey);
            Assert.True(strip.PillFor("justdrop")!.IsFocused, "Left from ramp focuses the Just Drop pill");
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
        finally { shell.Close(); global::ConditioningControlPanel.CoreSettings.ServiceProvider = null; }
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
    public Task MovedNoteHidesAfterRealHoldAndFadeTimers(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetUp();
        var strip = new SectionTabStrip { MotionOverride = level, MovedNoteTimerIntervalOverride = TimeSpan.FromMilliseconds(1) };
        var host = new Window { Content = strip };
        try
        {
            host.Show();
            strip.Show(NavSections.Studio, "presets");
            Assert.Equal(TimeSpan.FromSeconds(3.2), SectionTabStrip.MovedNoteHold);
            Assert.Equal(TimeSpan.FromMilliseconds(300), SectionTabStrip.MovedNoteFade);
            strip.ShowMovedNote("Moved");
            Assert.True(strip.MovedNoteShown);
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (strip.MovedNoteShown && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Yield();
            }
            Assert.False(strip.MovedNoteShown);
            Assert.Equal(0, strip.MovedNoteOpacity);
        }
        finally { host.Close(); }
    });
}
