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
        }
    });

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
