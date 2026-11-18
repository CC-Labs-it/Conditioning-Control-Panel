using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using System.Globalization;
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
        await AvaloniaTestDispatcher.RunAsync(() =>
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
                    Dispatcher.UIThread.RunJobs();   // the render clock is real time; no sleep (P08)
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
            return Task.CompletedTask;
        });
    }


    [Fact]
    public async Task TrayLabelsAndCrumbMatchWpf()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off };
            var w = new Window { Content = strip, Width = 1200, Height = 200 };
            try
            {
                w.Show();
                strip.Show(NavSections.Play, "play");
                w.UpdateLayout();
                var height = ((Control)strip.TrayHost.Parent!).Bounds.Height;
                var separator = strip.CrumbSeparator;
                var page = strip.CrumbPage;
                Assert.Equal(separator.FindResource("TextMutedBrush"), separator.Foreground);
                Assert.Equal(page.FindResource("TextLightBrush"), page.Foreground);
                var positions = strip.PillKeys.Select(k => strip.PillFor(k)!.TranslatePoint(new Point(0, 0), strip.TrayHost)!.Value.X).ToArray();
                foreach (var key in strip.PillKeys)
                {
                    var part = strip.Part(key)!;
                    var label = part.Label;
                    var width = new FormattedText(label.Text!, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(label.FontFamily, label.FontStyle, FontWeight.ExtraBold, label.FontStretch),
                        label.FontSize, Brushes.White).WidthIncludingTrailingWhitespace;
                    Assert.Equal(Math.Ceiling(width) + 1, label.MinWidth);
                    Assert.Equal(TextAlignment.Center, label.TextAlignment);
                    Assert.Equal(500, ToolTip.GetShowDelay(part.Pill));
                    Assert.Equal($"{Loc.Get("nav_door_play")} {Loc.Get("nav_crumb_sep")} {label.Text}", AutomationProperties.GetHelpText(part.Pill));
                }
                strip.Show(NavSections.Play, "blinktrainer");
                w.UpdateLayout();
                Assert.Equal(positions, strip.PillKeys.Select(k => strip.PillFor(k)!.TranslatePoint(new Point(0, 0), strip.TrayHost)!.Value.X).ToArray());
                strip.CrumbSection.Focus(NavigationMethod.Tab);
                Assert.Equal(new Thickness(2), strip.CrumbSection.BorderThickness);
                Assert.Equal(Color.Parse("#CCFFFFFF"), ((ISolidColorBrush)strip.CrumbSection.BorderBrush!).Color);
                var at = strip.CrumbSection.TranslatePoint(new Point(4, 4), w)!.Value;
                w.MouseMove(at);
                Assert.Equal(Color.Parse("#14FFFFFF"), ((ISolidColorBrush)strip.CrumbSection.Background!).Color);
                strip.Show(NavSections.Settings, "settings", "Settings");
                w.UpdateLayout();
                Assert.Equal(0, strip.TrayHost.Opacity);
                Assert.False(strip.TrayHost.IsHitTestVisible);
                Assert.Equal(height, ((Control)strip.TrayHost.Parent!).Bounds.Height);
            }
            finally { w.Close(); }
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
                // Studio's pills in table order; Just Drop has no window on this head, so no pill.
                Assert.Equal(new[] { "studio", "presets", "haptics", "ramp" }, strip.PillKeys);
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
