using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The rail's paint (WPF MainWindow.NavRail.cs, sync6-nav-rail-c): edge spur, art tint, label
/// glow, the 160 ms halo fade, NavBadges pills + the rich tooltip, the hover nudge, the landing glow
/// (GlowNavTarget / NavGlow) and the window-title crumb. Driven from the shell constructor.</summary>
public sealed class NavRailPaintTests
{
    private static void Run(MotionLevel motion, Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var lastBefore = s.NavLastTabBySection;
        var motionBefore = s.MotionLevel;
        s.NavLastTabBySection = null;
        s.MotionLevel = motion;
        NavBadges.ResetForTests();
        var w = new MainShellWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            body(w);
        }
        finally
        {
            w.Close();
            NavBadges.ResetForTests();
            s.NavLastTabBySection = lastBefore;
            s.MotionLevel = motionBefore;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static MainShellWindow.NavSectionRow Row(MainShellWindow w, string section) =>
        w.NavSectionRowsForTests.Single(r => r.Section == section);

    [Fact]
    public void LitRow_WearsSpurTintAndFadingHalo_LabelsGlow() => Run(MotionLevel.Full, w =>
    {
        w.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        var play = Row(w, NavSections.Play);
        var home = Row(w, NavSections.Home);
        Assert.True(play.Spur!.IsVisible);
        Assert.Equal(1.0, play.Spur.GetBaseValue(Visual.OpacityProperty).Value);   // fading in (160 ms)
        Assert.False(home.Spur!.IsVisible);
        Assert.Equal(MainShellWindow.NavArtTintActiveAlpha, ((ISolidColorBrush)play.Tint!.Background!).Color.A);
        Assert.Equal(0, ((ISolidColorBrush)home.Tint!.Background!).Color.A);
        var fade = Assert.IsType<DoubleTransition>(Assert.Single(play.Glow!.Transitions!));
        Assert.Equal(TimeSpan.FromMilliseconds(160), fade.Duration);
        foreach (var r in w.NavSectionRowsForTests)
            Assert.IsType<DropShadowEffect>(r.Label!.Effect);
    });

    [Fact]
    public void Badges_FollowNavBadges_DimOnceSeen_CapAtNine_TooltipCounts() => Run(MotionLevel.Off, w =>
    {
        var social = Row(w, NavSections.Social);
        Assert.False(social.Badge!.IsVisible);
        NavBadges.Set(NavSections.Social, 3);
        Dispatcher.UIThread.RunJobs();
        Assert.True(social.Badge.IsVisible);
        Assert.Equal("3", social.BadgeText!.Text);
        Assert.Equal(1.0, social.Badge.Opacity);
        Assert.IsType<DropShadowEffect>(social.Badge.Effect);   // fresh: raised chip

        social.Button.RaiseEvent(new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent));
        var lines = Assert.IsType<StackPanel>(ToolTip.GetTip(social.Button)).Children.OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Contains(Loc.GetF("nav_badge_open", 3), lines);
        Assert.Contains("Ctrl+" + MainShellWindow.NavShortcutNumber(NavSections.Social), lines);

        social.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // being on Social is seeing it
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(MainShellWindow.NavBadgeSeenOpacity, social.Badge.Opacity);
        Assert.Null(social.Badge.Effect);   // seen: the shadow drops

        NavBadges.Set(NavSections.Social, 12);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("9+", social.BadgeText.Text);
        NavBadges.Set(NavSections.Social, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.False(social.Badge.IsVisible);
    });

    [Theory]
    [InlineData(NavSections.Home)]
    [InlineData(NavSections.Social)]
    public void Hover_NudgesTheMedallionArt(string section) => Run(MotionLevel.Off, w =>
    {
        var home = Row(w, section);
        var at = home.Button.TranslatePoint(new Point(home.Button.Bounds.Width / 2, 20), w)!.Value;
        w.MouseMove(at, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(MainShellWindow.NavIconHoverScale, home.Art!.RenderTransform!.Value.M11, 3);
        w.MouseMove(new Point(2, 2), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.0, home.Art.RenderTransform!.Value.M11, 3);
    });

    private static int Glows(Visual v) =>
        AdornerLayer.GetAdornerLayer(v)?.Children.OfType<NavGlowRing>().Count(r => AdornerLayer.GetAdornedElement(r) == v) ?? 0;

    /// <summary>The glow's clock: 3 s of real time otherwise, and a slow runner (Windows CI, 11-17 s
    /// for this test) let it expire between two asserts.</summary>
    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static NavGlowRing[] Rings(Visual v) =>
        AdornerLayer.GetAdornerLayer(v)!.Children.OfType<NavGlowRing>().ToArray();

    [Fact]
    public void GlowNavKey_RingsTheRailRowAndPill_SettingsKeyRingsItsPill_OffDoesNothing() => Run(MotionLevel.Full, w =>
    {
        var clock = new SteppedClock();
        NavGlow.Time = clock;
        try { GlowNavKeyBody(w, clock); }
        finally { NavGlow.Time = TimeProvider.System; }
    });

    private static void GlowNavKeyBody(MainShellWindow w, SteppedClock clock)
    {
        w.ShowTab("playsessions");
        Dispatcher.UIThread.RunJobs();
        var play = Row(w, NavSections.Play).Button;
        var sessions = w.PageStrip!.PillFor("playsessions")!;
        // The zone landing rings its header after the scroll (WPF PlayTabView :73, ed48b817c).
        var zone = w.GetVisualDescendants().OfType<ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView>().Single().ZoneHeader("sessions")!;
        Assert.Equal(1, Glows(zone));
        w.GlowNavKey("playsessions");
        Assert.Equal(1, Glows(play));
        Assert.Equal(1, Glows(sessions));
        Assert.All(Rings(play), r => Assert.True(r.OnFrameBeat));   // the shell's shared frame beat, not a private timer (P82)

        w.ShowTab("appsettings");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, AdornerLayer.GetAdornerLayer(play)!.Children.OfType<NavGlowRing>().Count(r => AdornerLayer.GetAdornedElement(r) == sessions));   // the pill left: its glow left with it
        Assert.Equal(1, Glows(play));
        w.GlowNavKey("audio");
        var pill = w.AppSettingsPage!.FindControl<Control>("SectionPillAudio")!;
        Assert.Contains(AdornerLayer.GetAdornerLayer(pill)!.Children.OfType<NavGlowRing>(), r => AdornerLayer.GetAdornedElement(r) == pill);

        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        int before = Glows(play);
        w.GlowNavKey("play");
        Assert.Equal(before, Glows(play));

        // The cue: sheen up, hold at 0.8 (WPF NavGlow), then gone once its 3 s have run.
        clock.Now += TimeSpan.FromSeconds(1).Ticks;
        foreach (var r in Rings(play)) r.Tick();
        Assert.All(Rings(play), r => Assert.Equal(0.8, r.Opacity, 3));
        clock.Now += TimeSpan.FromMilliseconds(NavGlow.TotalMs(MotionLevel.Full)).Ticks;
        foreach (var r in Rings(play)) r.Tick();
        Assert.Equal(0, Glows(play));
    }

    [Fact]
    public void GlowCurve_FollowsWpfCues_ReducedHoldsStatic()
    {
        // WPF NavRail/NavGlow.cs: Full = 600 ms sheen, 2 s hold at 0.8, 400 ms fade; Reduced = 0.85 for 2 s.
        Assert.Equal(0.0, NavGlowRing.OpacityAt(MotionLevel.Full, 0), 3);
        Assert.Equal(1.0, NavGlowRing.OpacityAt(MotionLevel.Full, 300), 3);
        Assert.Equal(0.8, NavGlowRing.OpacityAt(MotionLevel.Full, 600), 3);
        Assert.Equal(0.8, NavGlowRing.OpacityAt(MotionLevel.Full, 2599), 3);
        Assert.Equal(0.4, NavGlowRing.OpacityAt(MotionLevel.Full, 2800), 3);   // half the fade
        Assert.Equal(0.0, NavGlowRing.OpacityAt(MotionLevel.Full, 3000), 3);
        Assert.Equal(3000, NavGlow.TotalMs(MotionLevel.Full));
        Assert.Equal(0.85, NavGlowRing.OpacityAt(MotionLevel.Reduced, 0), 3);
        Assert.Equal(0.85, NavGlowRing.OpacityAt(MotionLevel.Reduced, 1999), 3);
        Assert.Equal(2000, NavGlow.TotalMs(MotionLevel.Reduced));
        Assert.Equal(0, NavGlow.TotalMs(MotionLevel.Off));
    }

    [Fact]
    public void Title_NamesSectionAndPage_HomeDashboardNamesOnlyTheSection() => Run(MotionLevel.Off, w =>
    {
        const string Base = "Conditioning Control Panel";
        w.ShowTab("playsessions");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal($"{Base} - {Loc.Get(NavSections.Find(NavSections.Play)!.LabelKey)} {Loc.Get("nav_crumb_sep")} {Loc.Get(NavStripTable.PageLabelKey("playsessions")!)}", w.Title);
        w.ShowTab("settings");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal($"{Base} - {Loc.Get(NavSections.Find(NavSections.Home)!.LabelKey)}", w.Title);
    });
}
