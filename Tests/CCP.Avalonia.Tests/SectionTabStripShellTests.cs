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
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The section page header through the shell (WPF SectionTabStrip + MainWindow.SectionChrome.cs,
/// nav rework 2026-10-06): hidden on Home, Core's NavSections pills on a section page with the
/// tab on screen lit, a pill click / arrow key navigates, the section word returns to the
/// section's last tab, and Ctrl+K opens the palette whatever has focus (d858d6108).
/// </summary>
public sealed class SectionTabStripShellTests
{
    [Theory]
    [InlineData(MotionLevel.Off)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Full)]
    public async Task ChangedCrumbFadesAndAnUnchangedCrumbKeepsItsAnimation(MotionLevel level)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var strip = new SectionTabStrip { MotionOverride = level };
            var w = new Window { Content = strip };
            try
            {
                w.Show();
                strip.Show(NavSections.Studio, "presets");
                Assert.True(strip.CrumbFadeCompletion.IsCompleted);
                strip.Show(NavSections.Studio, "ramp");
                var fade = strip.CrumbFadeCompletion;
                Assert.Equal(level == MotionLevel.Off, fade.IsCompleted);
                strip.Show(NavSections.Studio, "ramp");
                Assert.Same(fade, strip.CrumbFadeCompletion);
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (!fade.IsCompleted && DateTime.UtcNow < deadline)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                Assert.True(fade.IsCompletedSuccessfully);
                Assert.Equal(1, strip.CrumbOpacity);
                strip.MotionOverride = MotionLevel.Off;
                strip.Show(NavSections.Studio, "presets");
                Assert.Equal(1, strip.CrumbOpacity);
                strip.Show(NavSections.Studio, "ramp"); // repeated Off changes after cancelling a fade
                Assert.Equal(1, strip.CrumbOpacity);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public async Task SettingsCrumbRereadsTheCheckedSectionAfterLanguageRebuild()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var language = CoreSettings.Current.Language;
            var w = new MainShellWindow();
            try
            {
                LocalizationManager.Instance.SetLanguage("en");
                w.Show();
                w.OpenAppSettingsSection("monitors");
                Dispatcher.UIThread.RunJobs();
                var before = w.PageStrip!.CrumbText;
                LocalizationManager.Instance.SetLanguage("de");
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(before, w.PageStrip.CrumbText);
                Assert.Equal($"{Loc.Get("nav_door_settings")} {Loc.Get("nav_crumb_sep")} {Loc.Get("settings_section_monitors")}", w.PageStrip.CrumbText);
            }
            finally { w.Close(); LocalizationManager.Instance.SetLanguage(language); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task StripFollowsShowTabAndItsPillsNavigate()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var s = CoreSettings.Current;
            var lastBefore = s.NavLastTabBySection;
            MainShellWindow? w = null;
            try
            {
                s.NavLastTabBySection = "";
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;

                w.ShowTab("settings");               // Home: the dashboard draws no header
                Assert.False(strip.IsVisible);

                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                Assert.True(strip.IsVisible);
                // Studio's pills in WPF table order, including the Just Drop window.
                Assert.Equal(new[] { "studio", "presets", "haptics", "justdrop", "ramp" }, strip.PillKeys);
                Assert.Equal("presets", strip.ActivePillKey);
                Assert.Equal($"{Loc.Get("nav_door_studio")} {Loc.Get("nav_crumb_sep")} {Loc.Get("tab_presets")}", strip.CrumbText);

                w.UpdateLayout();
                var hapticsAt = strip.PillFor("haptics")!.TranslatePoint(new Point(0, 0), strip);
                strip.PillFor("ramp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("ramp", w.CurrentTab);
                Assert.Equal("ramp", strip.ActivePillKey);
                // The page word has one width per section (WPF FixCrumbWidth): pills never move.
                w.UpdateLayout();
                Assert.Equal(hapticsAt, strip.PillFor("haptics")!.TranslatePoint(new Point(0, 0), strip));
                // One Tab stop for the strip: the active pill (WPF SetIsTabStop(p.Pill, on)).
                Assert.Equal(new[] { "ramp" }, strip.PillKeys.Where(k => strip.PillFor(k)!.IsTabStop));
                Assert.Equal("ramp", MainShellWindow.NavLastTabFor("studio"));

                // Arrow keys wrap and activate pages: Right from the last pill lands on the first.
                strip.PillFor("ramp")!.Focus();
                strip.PillFor("ramp")!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
                Assert.Equal("studio", w.CurrentTab);

                // Zone pages light their zone; the section word returns to the last tab.
                w.ShowTab("lockdown");
                Assert.Equal("playsessions", strip.ActivePillKey);
                w.ShowTab("presets");
                s.NavLastTabBySection = "{\"studio\":\"haptics\"}";
                var crumb = strip.GetLogicalDescendants().OfType<Button>().First(b => b.Name is null);
                crumb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("haptics", w.CurrentTab);

                // These routes already existed on the pinned base. Premium is hidden by the
                // WPF table; Companion's three V2 pages still use the room's live zones.
                w.ShowTab("companion");
                Assert.Equal(NavStripTable.Pills(NavSections.Companion).Select(t => t.Key), strip.PillKeys);
                foreach (var key in new[] { "personality", "permissions", "companionlinks", "companionai" })
                {
                    w.ShowTab(key);
                    Assert.Equal(key, strip.ActivePillKey);
                    Assert.Equal(key, w.CurrentTab);
                }
                w.ShowTab("exclusives");
                Assert.Equal("premium", w.CurrentTab);
                Assert.False(strip.IsVisible); // Home has no strip, including its hidden Premium page
                Assert.Contains(Loc.Get("nav_tab_premium"), w.Title);

                // Library launchers stay pills that open, not pages.
                w.ShowTab("assets");
                Assert.Contains("medialog", strip.PillKeys);
                Assert.Contains("folders", strip.PillKeys);

                // Ctrl+K from inside the page, through a control that would otherwise take the key.
                Assert.False(SettingsPaletteWindow.IsOpen);
                strip.PillFor("assets")!.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
                Assert.True(SettingsPaletteWindow.IsOpen);
            }
            finally
            {
                SettingsPaletteWindow.CloseIfOpen();
                w?.Close();
                s.NavLastTabBySection = lastBefore;
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
