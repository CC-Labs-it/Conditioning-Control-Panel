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

    // The PR's WindowPillAsksAndOnlyYesRequestsTheWindow pinned its own fade/hover mechanics; this head keeps the base
    // card (beat-loop fade, face dims on hover), covered by WindowConfirmationParityTests and SyncSixTipAndWindowTests.

    // Declutter wave 2 (2026-11-15): Just Drop, the only window pill, left the Studio strip for
    // the launcher's Tools door, so no pill asks "Open it?" any more (the confirmation card stays
    // in SectionTabStrip for a future window pill). ShowTab("justdrop") still opens the shop.
    [Fact]
    public Task JustDropHasNoPillAndAPlainPillStillGoesAtOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetUp();
        var service = new global::ConditioningControlPanel.Services.SettingsService();
        global::ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var st = global::ConditioningControlPanel.CoreSettings.Current;
        (st.Welcomed, st.HasAcceptedAgeVerification, st.WhatMovedCardShown) = (true, true, 1);   // P52/P76: no intro takes focus
        global::ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(true);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("presets");
            Dispatcher.UIThread.RunJobs();
            var strip = shell.PageStrip!;
            Assert.DoesNotContain("justdrop", strip.PillKeys);
            Assert.Equal(NavSections.Studio, NavSections.SectionForTab("justdrop"));
            Click(strip.PillFor("ramp")!);
            Assert.Null(strip.ConfirmingKey);
            Assert.Equal("ramp", shell.CurrentTab);
        }
        finally
        {
            shell.Close(); global::ConditioningControlPanel.CoreSettings.ServiceProvider = null;
            global::ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(false);
        }
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
