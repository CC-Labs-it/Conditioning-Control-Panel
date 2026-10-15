using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class QuestsDepthTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static void Run(Action<QuestsTabView, Window, Clock> test) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
        {
            var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
        var oldSettings = CoreSettings.ServiceProvider;
        var oldTime = BeatLoop.Time;
        var clock = new Clock();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = MotionLevel.Off;
        BeatLoop.Time = clock;
        Window? window = null;
        try
        {
            var view = new QuestsTabView();
            window = new Window { Content = view, Width = 1400, Height = 1000 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            test(view, window, clock);
        }
        finally
        {
            window?.Close();
            BeatLoop.Time = oldTime;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private static T Named<T>(QuestsTabView view, string name) where T : Control =>
        Assert.IsType<T>(view.FindControl<T>(name));
    private static double Y(Control control) => Assert.IsType<TranslateTransform>(control.RenderTransform).Y;

    [Fact]
    public void CompletedSeatsSinkAndFloatBandsFollowLiveOverlays() => Run((v, w, _) =>
    {
        for (int i = 0; i < 3; i++)
        {
            var seat = Named<Grid>(v, "DailySeat" + i);
            var band = Named<Border>(v, "DailySeatBand" + i);
            var card = Named<DailyQuestCard>(v, "DailyCard" + i);
            Assert.Equal(0, Y(seat));
            Assert.True(band.IsVisible);
            card.Apply(new DailyQuestCardModel { Slot = i, IsCompleted = true, Target = 5, Current = 5 });
            Assert.Equal(DepthLaw.ActiveSinkPx, Y(seat));
            Assert.False(band.IsVisible);
            card.ShowEmpty(i, "empty", "none");
            Assert.Equal(0, Y(seat));
            Assert.True(band.IsVisible);
        }
        var overlay = Named<Border>(v, "WeeklyCompletedOverlay");
        overlay.IsVisible = true;
        Assert.Equal(DepthLaw.ActiveSinkPx, Y(Named<Grid>(v, "WeeklySeat")));
        Assert.False(Named<Border>(v, "WeeklyFloatBand").IsVisible);
        Assert.Same(Application.Current!.FindResource("DepthPressedBevel"), Named<Border>(v, "WeeklyQuestCard").BorderBrush);
        Assert.Equal(-6, Assert.IsType<RotateTransform>(Named<Grid>(v, "WeeklyDoneStamp").RenderTransform).Angle);
        overlay.IsVisible = false;
        Assert.Equal(0, Y(Named<Grid>(v, "WeeklySeat")));
        Assert.True(Named<Border>(v, "WeeklyFloatBand").IsVisible);
    });

    [Fact]
    public void WeeklyTubeFitsItsGrooveAndHueRepaintsEveryBand() => Run((v, w, _) =>
    {
        Assert.Same(Application.Current!.FindResource("DepthWellFloorBrush"), Named<Border>(v, "DailyWell").Background);
        var fill = Named<Border>(v, "WeeklyProgressFill");
        var bead = Named<Ellipse>(v, "WeeklyTubeBead");
        Assert.False(bead.IsVisible);
        foreach (double width in new[] { 120.0, 4, 10 })
        {
            fill.Width = width;
            w.UpdateLayout();
            Assert.Equal(width >= 10, bead.IsVisible);
        }
        Assert.Equal(8, bead.Width);
        Assert.Equal(4, Named<Border>(v, "WeeklyGrooveTop").Height);
        Assert.Same(Application.Current.FindResource("DepthTubeGloss"), Named<Border>(v, "WeeklyTubeGloss").Background);
        var hue = Color.Parse("#123456");
        typeof(QuestsTabView).GetMethod("PaintDepthQuests", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(v, new object[] { hue });
        foreach (var name in new[] { "DailySeatBand0", "DailySeatBand1", "DailySeatBand2", "WeeklyFloatBand", "DailyCounterDrop", "WeeklyXpDrop", "WeeklyRerollDrop", "FixStreakDrop" })
        {
            var brush = Assert.IsType<LinearGradientBrush>(Named<Border>(v, name).Background);
            double alpha = name.Contains("Band") ? DepthLaw.FloatAlpha : DepthLaw.ShadowAlpha;
            Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), alpha)), brush.GradientStops[0].Color);
            Assert.Equal(0, brush.GradientStops[1].Color.A);
            Assert.False(Named<Border>(v, name).IsHitTestVisible);
        }
    });

    [Fact]
    public void ShellSectionWashRepaintsTheQuestsBands() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;   // restored by the isolation hook
        global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? w = null;
        try
        {
            w = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            w.Show();
            w.ShowTab("presets");   // a section other than Quests' own (You), which InitDepth paints
            Dispatcher.UIThread.RunJobs();
            var hue = NavStripRules.Accent(NavSections.SectionForTab("presets"));
            Assert.NotEqual(NavStripRules.Accent(NavSections.You), hue);
            var quests = w.Named<QuestsTabView>("QuestsTab")!;
            var brush = Assert.IsType<LinearGradientBrush>(Named<Border>(quests, "DailySeatBand0").Background);
            Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.FloatAlpha)), brush.GradientStops[0].Color);
        }
        finally { w?.Close(); }
    });

    [Fact]
    public void BothPlanksPressReleaseAndStopTheirFiniteBeatOnDetach() => Run((v, w, clock) =>
    {
        foreach (var prefix in new[] { "WeeklyReroll", "FixStreak" })
        {
            var button = Named<Button>(v, prefix == "WeeklyReroll" ? "BtnRerollWeekly" : "BtnFixStreak");
            button.IsVisible = button.IsEnabled = true;
            w.UpdateLayout();
            var face = Named<Grid>(v, prefix + "Face");
            var drop = Named<Border>(v, prefix + "Drop");
            button.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), w)!.Value;
            w.MouseMove(point);
            Assert.Equal(-DepthLaw.HoverLiftPx, Y(face));
            Assert.Equal(DepthLaw.RaisedPx + DepthLaw.HoverLiftPx, drop.Margin.Top);
            w.MouseDown(point, MouseButton.Left);
            Assert.True(button.IsPressed);
            Assert.Equal(DepthLaw.PressTravelPx, Y(face));
            Assert.Equal(0, drop.Opacity);
            var level = prefix == "WeeklyReroll" ? MotionLevel.Full : MotionLevel.Reduced;
            CoreSettings.Current.MotionLevel = level;
            w.MouseMove(new Point(-20, -20));
            w.MouseUp(new Point(-20, -20), MouseButton.Left); // cancel the click; only the travel is under test
            Assert.False(button.IsPressed);
            var releaseMs = DepthLaw.Ms(DepthLaw.ReleaseMs, level);
            clock.Now += TimeSpan.FromMilliseconds(releaseMs * 0.6).Ticks;
            BeatLoop.StepAll();
            Assert.Equal(-DepthLaw.ReleaseOvershootPx, Y(face), 5);
            clock.Now += TimeSpan.FromMilliseconds(releaseMs * 0.4).Ticks;
            BeatLoop.StepAll();
            Assert.Equal(0, Y(face), 5);
            Assert.Equal(3, drop.Margin.Top);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            button.IsEnabled = false;
            Assert.Equal(0.5, Named<Border>(v, prefix + "Bevel").Opacity);
        }
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var reroll = Named<Button>(v, "BtnRerollWeekly");
        reroll.IsEnabled = true;
        reroll.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        var detachPoint = reroll.TranslatePoint(new Point(reroll.Bounds.Width / 2, reroll.Bounds.Height / 2), w)!.Value;
        w.MouseMove(detachPoint);
        w.MouseDown(detachPoint, MouseButton.Left);
        var activeFace = Named<Grid>(v, "WeeklyRerollFace");
        Assert.Contains(BeatLoop.Running, loop => loop.Owner == activeFace);
        v.IsVisible = false;
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == activeFace);
        w.MouseUp(new Point(-20, -20), MouseButton.Left);
        v.IsVisible = true;
        w.UpdateLayout();
        w.MouseMove(detachPoint);
        w.MouseDown(detachPoint, MouseButton.Left);
        Assert.Contains(BeatLoop.Running, loop => loop.Owner == activeFace);
        w.Content = null;
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == Named<Grid>(v, "WeeklyRerollFace"));
    });
}
