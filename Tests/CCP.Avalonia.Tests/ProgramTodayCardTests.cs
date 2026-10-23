using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Programs slice 4: the dashboard's "Today" card (WPF RefreshProgramTodayCard, ProgramsTab.cs:2251)
/// and its banner decoration (WPF MainWindow.ProgramBanner.cs). Read-only: nothing is written.
/// </summary>
public sealed class ProgramTodayCardTests
{
    [Fact]
    public void DashboardCardShowsTodayForAnActiveRunAndItsLoopsStopWhenHidden() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var program = BuiltInPrograms.All().First(p => p.Id == "first_week");
        var savedDay = DateTime.Today;
        var state = new ProgramState
        {
            Active = new ProgramEnrollment
            {
                ProgramId = program.Id, StartedAt = savedDay.AddDays(-1), CurrentDay = 2,
                CurrentDayDate = savedDay, State = ProgramEnrollmentState.Active,
            }
        };
        state.Active.GetOrCreateRecord(1, savedDay.AddDays(-1)).DayCompleted = true;
        state.Active.GetOrCreateRecord(2, savedDay);

        var dir = Directory.CreateTempSubdirectory("ccp-programs-today-").FullName;
        var path = System.IO.Path.Combine(dir, "programs.json");
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        var bytes = File.ReadAllBytes(path);

        var oldPrograms = AvApp.Programs;
        var settings = CoreSettings.Current;
        var (motion, perf) = (settings.MotionLevel, settings.PerformanceMode);
        var (seenTab, seenIntro) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro);
        settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
        settings.MotionLevel = MotionLevel.Full;
        settings.PerformanceMode = false;
        MainShellWindow? shell = null;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        try
        {
            AvApp.Programs = new ProgramService(path, readOnly: true);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show();
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            var dash = shell.Named<SettingsTabView>("SettingsTab")!;
            T F<T>(string n) where T : Control => dash.FindControl<T>(n)!;
            dash.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // The card: title, then "Day 2 · <day title> · 1 session, N tasks remaining" (WPF :2286-2307).
            var card = F<Button>("ProgramTodayCard");
            Assert.True(card.IsEffectivelyVisible);
            Assert.Equal(program.Title, F<TextBlock>("TxtProgramTodayTitle").Text);
            var line = F<TextBlock>("TxtProgramTodayLine").Text!;
            Assert.StartsWith(Loc.GetF("programs_card_day", 2) + "  ·  " + program.GetDay(2)!.Title + "  ·  ", line);
            Assert.Contains(Loc.Get("programs_card_session_left"), line);
            Assert.Equal(Color.Parse(program.AccentColor!), ((ISolidColorBrush)card.BorderBrush!).Color);

            // Banner: the program's own art, plus five sparkles, and the ambient loops on screen.
            Assert.True(F<Rectangle>("ProgramTodayArt").IsVisible);
            Assert.Equal(5, F<Canvas>("ProgramTodaySparkles").Children.Count);
            Assert.True(dash.ProgramBannerFxRunning);
            BannerLoopsFollowWpfAndTheWidth(shell, dash, time);

            // P01: the shell hides the tab, not the card -> the loops stop; showing it restarts them.
            dash.IsVisible = false;
            Assert.False(dash.ProgramBannerFxRunning);
            Assert.False(F<Rectangle>("ProgramTodayFog").IsVisible);
            dash.IsVisible = true;
            Assert.True(dash.ProgramBannerFxRunning);

            // Motion gate: Reduced stops them too.
            settings.MotionLevel = MotionLevel.Reduced;
            ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(dash.ProgramBannerFxRunning);
            settings.MotionLevel = MotionLevel.Full;
            ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(dash.ProgramBannerFxRunning);

            // Clicking the card opens the Programs run view (WPF ProgramTodayCard_Click -> ShowTab).
            card.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Named<ProgramsTabView>("ProgramsTab")!.IsVisible);
            Assert.False(dash.ProgramBannerFxRunning);

            // P65: back on the dashboard the loops run again; closing the window detaches and stops them.
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.True(dash.ProgramBannerFxRunning);
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(dash.ProgramBannerFxRunning);

            // No enrollment: the card collapses (WPF :2265-2268).
            AvApp.Programs.Dispose();
            AvApp.Programs = null;
            dash.RefreshProgramTodayCard();
            Assert.False(card.IsVisible);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            AvApp.Programs?.Dispose();
            AvApp.Programs = oldPrograms;
            (settings.MotionLevel, settings.PerformanceMode) = (motion, perf);
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro) = (seenTab, seenIntro);
            Directory.Delete(dir, true);
            BeatLoop.Time = TimeProvider.System;
        }
    });

    /// <summary>The banner loops on the 30 fps beat, stepped (P08), on WPF MainWindow.ProgramBanner.cs's
    /// curves, read against the card's CURRENT width (a resize mid-run used to keep the first width), and
    /// the hover nudge really slides the art (it was an Animation on a Transform: inert, P82).</summary>
    private static void BannerLoopsFollowWpfAndTheWidth(MainShellWindow shell, SettingsTabView dash, SteppedTime time)
    {
        T F<T>(string n) where T : Control => dash.FindControl<T>(n)!;
        void Step(double s) { time.Advance(TimeSpan.FromSeconds(s)); BeatLoop.StepAll(); }
        var decor = F<Grid>("ProgramTodayDecor");
        var fog = (TranslateTransform)F<Rectangle>("ProgramTodayFog").RenderTransform!;
        var sheen = (TranslateTransform)F<Rectangle>("ProgramTodaySheen").RenderTransform!;
        var accent = F<Border>("ProgramTodayAccent");
        var dots = F<Canvas>("ProgramTodaySparkles").Children.OfType<Ellipse>().ToArray();
        var mine = BeatLoop.Running.Where(l => l.Owner == F<Button>("ProgramTodayCard")).ToArray();
        Assert.Single(mine);
        Assert.True(mine[0].OnFrameBeat, "the banner loop is not on the window's frames");   // StepAll skips the clock
        Assert.Equal(0.55, accent.Opacity, 3);
        Assert.Equal(-160, sheen.X, 3);
        Assert.All(dots, d => Assert.Equal(0, d.Opacity, 3));

        Step(0.85);                                                          // half the 1.7 s sweep (:317)
        var w = decor.Bounds.Width;
        Assert.Equal(-160 + (w + 220) * 0.5, sheen.X, 2);
        Assert.Equal(0.9 * 0.45 / 0.85, dots[0].Opacity, 3);                 // fading, 0.9 at 0.45 -> 0 at 1.3 (:452-456)
        Assert.Equal(0, dots[1].Opacity, 3);                                 // still before its 1.15 s BeginTime
        Step(3.4 - 0.85);
        Assert.Equal(1.0, accent.Opacity, 3);                                // 0.55 <-> 1 over 3.4 s (:333)

        var card = F<Button>("ProgramTodayCard");
        card.Width = w - 300;                                                // a resize mid-run (the shell scales a fixed design width)
        shell.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var w2 = decor.Bounds.Width;
        Assert.True(Math.Abs(w2 - w) > 50, $"the card did not resize ({w} -> {w2})");
        Step(11.85 - 3.4);
        Assert.Equal(-160 + (w2 + 220) * 0.5, sheen.X, 2);                   // the next sweep spans the new width
        Assert.Equal(-w2 * 0.55 * BeatLoop.Breath(11.85, 26), fog.X, 2);     // 26 s each way, sine (:303)
        card.Width = double.NaN;
        shell.UpdateLayout();

        // Hover nudge (:478-490): 260 ms quadratic ease-out to -10, per rendered frame.
        var art = F<Rectangle>("ProgramTodayArt");
        typeof(SettingsTabView).GetMethod("NudgeProgramBannerArt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(dash, new object[] { -10.0 });                               // what PointerEntered calls
        Assert.True(art.IsVisible, "no banner art - the nudge test proves nothing");
        double Nudged(double seconds)
        {
            time.Advance(TimeSpan.FromSeconds(seconds));
            // A forced tick only brings a frame round when a composition batch is in flight. Under load the
            // headless 60 fps wall-clock tick can re-run the nudge at an unchanged X (no commit), leaving the
            // next frame on MediaContext's 16 ms wall-clock timer, so the tick below delivered nothing
            // (-7.5 vs -10). Requesting a commit makes this one deterministic frame either way.
            art.InvalidateVisual();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            return ((TranslateTransform)art.RenderTransform!).X;
        }
        var p = 0.13 / 0.26;
        Assert.Equal(-10 * (1 - (1 - p) * (1 - p)), Nudged(0.13), 3);       // halfway in time, 75% of the way
        Nudged(0.2);
        Assert.Equal(-10, ((TranslateTransform)art.RenderTransform!).X, 3);
    }

    [Fact]
    public void BannerFallsBackToTheDefaultArtWhenAProgramShipsNone()
    {
        var bare = new ProgramDefinition { Id = "zz_no_art_program" };
        Assert.Equal("programs/banner_default.png", ProgramArtPaths.Banner(bare).Last());
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var dash = new SettingsTabView();
            dash.ApplyProgramBannerArt(bare, Brushes.HotPink);
            var art = dash.FindControl<Rectangle>("ProgramTodayArt")!;
            Assert.True(art.IsVisible);
            Assert.NotNull(((ImageBrush)art.Fill!).Source);
        });
    }
}
