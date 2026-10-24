using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Nav polish waves 3/7/10 on the section strip, through the shell (twin of WPF
/// NavStripPolishGlyphTests / SectionTabStripFxTests / PageDepthTests): the leading glyph, the
/// sunken tray and pill drop bands, the pressed-in lit pill, the hover lift, the choose burst and
/// the idle sheen clock (motion-gated, stopped when hidden or detached).
/// </summary>
public sealed class SectionStripDepthFxShellTests
{
    private static Color Solid(IBrush? b) => Assert.IsAssignableFrom<ISolidColorBrush>(b).Color;

    private static (MainShellWindow W, SectionTabStrip Strip) Open(string tab)
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var w = new MainShellWindow();
        w.Show();
        w.ShowTab(tab);
        Dispatcher.UIThread.RunJobs();
        HeadlessFrame.Publish(w);
        return (w, w.Named<SectionTabStrip>("SectionStrip")!);
    }

    [Fact]
    public async Task PillsWearTheirFluentGlyphInTheTabTintAndTheTrayIsSunken()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;   // restored by the isolation hook
            MainShellWindow? w = null;
            try
            {
                (w, var strip) = Open("presets");
                var hue = NavStripRules.Accent(NavSections.SectionForTab("presets"));

                // Glyph (wave 3): every pill IconMap knows wears its Fluent icon, rest in its tint.
                foreach (var key in strip.PillKeys)
                {
                    var p = strip.Part(key)!;
                    var icon = IconMap.ForTab(key);
                    if (icon == null) { Assert.Null(p.Glyph); continue; }
                    Assert.Equal(icon.Value.Kind, p.Glyph!.Kind);
                    Assert.Equal(NavStripRules.GlyphSize, p.Glyph.Size);
                }
                var studio = strip.Part("studio")!;
                var presets = strip.Part("presets")!;
                Assert.Equal(NavStripRules.RestGlyphOn(hue, studio.Tint), Solid(studio.Glyph!.Foreground));
                Assert.Equal(NavStripRules.ActiveGlyphOn(hue, presets.Tint), Solid(presets.Glyph!.Foreground));

                // Depth (wave 10): the sunken tray's floor and its 9 px absolute top band.
                Assert.Equal(Color.Parse("#FF120D20"), Solid(strip.TrayFloorBrush));
                var top = Assert.IsType<LinearGradientBrush>(strip.TrayWellTopBrush);
                Assert.Equal(RelativeUnit.Absolute, top.EndPoint.Unit);
                Assert.Equal(DepthLaw.WellPx, top.EndPoint.Point.Y);
                Assert.Equal(Color.Parse("#A6000000"), top.GradientStops[0].Color);

                // A rest pill stands RaisedPx off the sheet on a hue-tinted drop (0x9E = ShadowAlpha).
                Assert.Equal(0, strip.PillFaceTravel("studio"));
                Assert.Equal(DepthLaw.RaisedPx, strip.PillDropLength("studio"));
                var drop = Assert.IsType<LinearGradientBrush>(strip.PillDropBrush("studio"));
                Assert.Equal(0x9E, drop.GradientStops[0].Color.A);
                Assert.Equal(NavStripPaint.Mix(DepthLaw.NeutralShadowInk, hue.ToUInt32(), DepthLaw.ShadowHuePull) & 0xFFFFFF,
                    drop.GradientStops[0].Color.ToUInt32() & 0xFFFFFF);

                // The lit pill is pressed in: face sunk, no drop, the fill sunk with the pressed bevel.
                Assert.Equal(DepthLaw.ActiveSinkPx, strip.PillFaceTravel("presets"));
                Assert.Equal(0, strip.PillDropLength("presets"));
                Assert.Equal(DepthLaw.ActiveSinkPx, strip.ActiveFillSink);
                Assert.IsType<LinearGradientBrush>(strip.ActivePressLayer.BorderBrush);

                // Pressing a rest pill drops it PressTravelPx and takes its drop band away.
                strip.DepthPressForTests("studio", true);
                Assert.Equal(DepthLaw.PressTravelPx, strip.PillFaceTravel("studio"));
                Assert.Equal(0, strip.PillDropLength("studio"));
                strip.DepthPressForTests("studio", false);
                Assert.Equal(0, strip.PillFaceTravel("studio"));

                // Choosing moves the glyph colours and the sink with the lit pill.
                studio.Pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(NavStripRules.ActiveGlyphOn(hue, studio.Tint), Solid(studio.Glyph!.Foreground));
                Assert.Equal(NavStripRules.RestGlyphOn(hue, presets.Tint), Solid(presets.Glyph!.Foreground));
                Assert.Equal(DepthLaw.ActiveSinkPx, strip.PillFaceTravel("studio"));
                Assert.Equal(DepthLaw.RaisedPx, strip.PillDropLength("presets"));
            }
            finally { w?.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HoverLiftsByMotionLevelChooseBurstsAndTheSheenClockFollowsVisibility()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;   // restored by the isolation hook
            MainShellWindow? w = null;
            try
            {
                (w, var strip) = Open("presets");
                // Keep the real nine-second timer parked until its lifecycle is tested below.
                strip.AmbientTierOverride = false;
                strip.UpdateSheenClock();

                // The real pointer path (P04): moving onto a rest pill lifts it, pressing drops
                // its face, releasing springs it back and chooses it.
                var ramp0 = strip.Part("ramp")!.Pill;
                var at = ramp0.TranslatePoint(new Point(ramp0.Bounds.Width / 2, ramp0.Bounds.Height / 2), w)!.Value;
                w.MouseMove(at);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(-NavStripMotion.HoverLiftPx, strip.PillHover("ramp").Lift);
                w.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, strip.PillHover("ramp").Lift);
                w.MouseMove(at);
                Dispatcher.UIThread.RunJobs();
                w.MouseDown(at, MouseButton.Left);
                Assert.Equal(DepthLaw.PressTravelPx, strip.PillFaceTravel("ramp"));
                w.MouseUp(at, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("ramp", strip.ActivePillKey);
                Assert.Equal(DepthLaw.ActiveSinkPx, strip.PillFaceTravel("ramp"));
                w.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                strip.Part("presets")!.Pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                strip.FxHoverForTests("presets", false);
                w.UpdateLayout();

                // Hover (wave 7): Full lifts and grows, Reduced lifts only, Off never moves; the
                // lit pill rides the fill and keeps still. Hover lengthens the drop band.
                strip.FxHoverForTests("studio", true);
                Assert.Equal((-NavStripMotion.HoverLiftPx, NavStripMotion.HoverScale), strip.PillHover("studio"));
                Assert.Equal(DepthLaw.RaisedPx + DepthLaw.HoverLiftPx, strip.PillDropLength("studio"));
                strip.FxHoverForTests("studio", false);
                Assert.Equal((0d, 1d), strip.PillHover("studio"));
                strip.FxHoverForTests("presets", true);
                Assert.Equal((0d, 1d), strip.PillHover("presets"));
                CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
                strip.FxHoverForTests("ramp", true);
                Assert.Equal((-NavStripMotion.HoverLiftPx, 1d), strip.PillHover("ramp"));
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                strip.FxHoverForTests("haptics", true);
                Assert.Equal((0d, 1d), strip.PillHover("haptics"));

                // Choose (wave 7): a burst in the hue at Full, none at Off.
                int bursts = strip.BurstsFired;   // the pointer path above chose two pills at Full
                Assert.Equal(2, bursts);
                strip.Part("ramp")!.Pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(bursts, strip.BurstsFired);
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
                w.UpdateLayout();
                strip.Part("studio")!.Pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(bursts + 1, strip.BurstsFired);

                // Sheen (wave 7): one sweep across the lit pill; the clock runs only on Full with
                // an ambient tier while shown and attached (P01/P65).
                w.UpdateLayout();
                Assert.True(strip.SweepSheen());
                Assert.Equal(1, strip.SheensRun);
                strip.AmbientTierOverride = true;
                strip.UpdateSheenClock();
                Assert.True(strip.SheenClockRunning);
                strip.IsVisible = false;
                Assert.False(strip.SheenClockRunning);
                strip.IsVisible = true;
                Assert.True(strip.SheenClockRunning);
                strip.AmbientTierOverride = false;
                strip.UpdateSheenClock();
                Assert.False(strip.SheenClockRunning);
                strip.AmbientTierOverride = true;
                strip.UpdateSheenClock();
                w.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.False(strip.SheenClockRunning);
                w = null;
            }
            finally { w?.Close(); }
            return Task.CompletedTask;
        });
    }
}
