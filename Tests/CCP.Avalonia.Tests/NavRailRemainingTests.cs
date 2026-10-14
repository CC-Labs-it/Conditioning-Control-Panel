using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavRailRemainingTests
{
    private static void Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreMods.MakeModAwareProvider;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var motion = CoreSettings.Current.MotionLevel;
        MainShellWindow? window = null;
        try
        {
            LocalizationManager.Instance.SetLanguage("en");
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            window = new MainShellWindow();
            window.Show(); Dispatcher.UIThread.RunJobs();
            body(window);
        }
        finally
        {
            window?.Close();
            CoreMods.MakeModAwareProvider = previous;
            CoreSettings.Current.MotionLevel = motion;
            LocalizationManager.Instance.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void RailLabelsFollowModSwitchAndLanguageWithoutLosingTheirBindings() => Run(w =>
    {
        var loc = LocalizationManager.Instance;
        var home = w.NavSectionRowsForTests.Single(r => r.Section == NavSections.Home).Label!;
        var studio = w.NavSectionRowsForTests.Single(r => r.Section == NavSections.Studio).Label!;
        var homeEnglish = loc.GetEnglish(NavSections.Find(NavSections.Home)!.LabelKey)!;
        var searchEnglish = loc.GetEnglish("nav_search_label")!;
        CoreMods.MakeModAwareProvider = text => text == homeEnglish ? "The nest" : text == searchEnglish ? "Seek" : text;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("THE NEST", home.Text);
        Assert.Equal("Seek", w.Named<TextBlock>("TxtNavSearchLabel")!.Text);

        loc.SetLanguage("de"); Dispatcher.UIThread.RunJobs();
        Assert.Equal("THE NEST", home.Text); // a changed English label overrides localization
        Assert.Equal(loc.Get(NavSections.Find(NavSections.Studio)!.LabelKey).ToUpper(System.Globalization.CultureInfo.CurrentUICulture), studio.Text);
        CoreMods.MakeModAwareProvider = text => text;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(loc.Get(NavSections.Find(NavSections.Home)!.LabelKey).ToUpper(System.Globalization.CultureInfo.CurrentUICulture), home.Text);
        Assert.Equal(loc.Get("nav_search_label"), w.Named<TextBlock>("TxtNavSearchLabel")!.Text);
    });

    [Fact]
    public void RailRingsRaiseIdleHueAndFlipTheBevelForTheSelectedSocket() => Run(w =>
    {
        var studio = w.NavSectionRowsForTests.Single(r => r.Section == NavSections.Studio);
        var idle = Assert.IsType<LinearGradientBrush>(studio.Ring!.BorderBrush);
        Assert.Equal(new[] { 0.0, 0.42, 1.0 }, idle.GradientStops.Select(s => s.Offset));
        Assert.All(idle.GradientStops, stop => Assert.Equal(0xCC, stop.Color.A));
        Assert.True(idle.GradientStops[0].Color.R + idle.GradientStops[0].Color.G + idle.GradientStops[0].Color.B >
                    idle.GradientStops[2].Color.R + idle.GradientStops[2].Color.G + idle.GradientStops[2].Color.B);
        Assert.Equal(new Thickness(3), studio.Ring.BorderThickness);
        var at = studio.Button.TranslatePoint(new Point(studio.Button.Bounds.Width / 2, 20), w)!.Value;
        w.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.All(Assert.IsType<LinearGradientBrush>(studio.Ring.BorderBrush).GradientStops, stop => Assert.Equal(0xF2, stop.Color.A));
        w.ShowTab("studio"); Dispatcher.UIThread.RunJobs();
        var selected = Assert.IsType<LinearGradientBrush>(studio.Ring.BorderBrush);
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, selected.GradientStops.Select(s => s.Offset));
        Assert.All(selected.GradientStops, stop => Assert.Equal(0xFF, stop.Color.A));
        Assert.True(selected.GradientStops[0].Color.R + selected.GradientStops[0].Color.G + selected.GradientStops[0].Color.B <
                    selected.GradientStops[2].Color.R + selected.GradientStops[2].Color.G + selected.GradientStops[2].Color.B);
        Assert.Equal(new Thickness(3.5), studio.Ring.BorderThickness);
    });
}
