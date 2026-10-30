using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
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
    public void RailShadowFollowsUserNavigationAndDoesNotInterceptThePage() => Run(w =>
    {
        var shadow = w.Named<global::Avalonia.Controls.Shapes.Rectangle>("NavRailShadow")!;
        Assert.NotNull(shadow);
        Assert.Equal(DepthLaw.RailShadowPx, shadow.Width);
        Assert.False(shadow.IsHitTestVisible);
        Assert.Equal(1, Grid.GetColumn(shadow));
        Assert.Equal(1, Grid.GetRow(shadow));
        Assert.Equal(5, Grid.GetRowSpan(shadow));
        Assert.Equal(HorizontalAlignment.Left, shadow.HorizontalAlignment);
        Assert.Equal(59, shadow.ZIndex);
        Assert.True(shadow.Bounds.Height > 0);
        foreach (var section in new[] { NavSections.Studio, NavSections.Play, NavSections.Home })
        {
            var button = w.NavSectionRowsForTests.Single(r => r.Section == section).Button;
            button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(section, NavSections.SectionForTab(w.CurrentTab));
            var brush = Assert.IsType<LinearGradientBrush>(shadow.Fill);
            Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), brush.StartPoint);
            Assert.Equal(new RelativePoint(1, 0, RelativeUnit.Relative), brush.EndPoint);
            Assert.Equal(new[] { 0.0, 1.0 }, brush.GradientStops.Select(s => s.Offset));
            var hue = NavStripPaint.Accent(section);
            Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue, 0xBF / 255.0)), brush.GradientStops[0].Color);
            Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue, 0)), brush.GradientStops[1].Color);
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
        var backEnglish = loc.GetEnglish("nav_back_label")!;
        var webEnglish = loc.GetEnglish("nav_door_webapp")!;
        CoreMods.MakeModAwareProvider = text => text == homeEnglish ? "The nest" : text == searchEnglish ? "Seek"
            : text == backEnglish ? "Retreat" : text == webEnglish ? "Hive" : text;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("THE NEST", home.Text);
        Assert.Equal("Seek", w.Named<TextBlock>("TxtNavSearchLabel")!.Text);
        Assert.Equal("Retreat", w.Named<TextBlock>("TxtNavBackLabel")!.Text);
        Assert.Equal("Hive", w.Named<TextBlock>("TxtNavWebAppLabel")!.Text);

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
    public void AModChangeQueuedBeforeCloseDoesNotRebindTheClosedRail() => Run(w =>
    {
        var back = w.Named<TextBlock>("TxtNavBackLabel")!;
        var backEnglish = LocalizationManager.Instance.GetEnglish("nav_back_label")!;
        CoreMods.MakeModAwareProvider = text => text == backEnglish ? "Retreat" : text;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false)); // posts the relabel
        w.Close(); // disposes the label bindings
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("Retreat", back.Text);
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
