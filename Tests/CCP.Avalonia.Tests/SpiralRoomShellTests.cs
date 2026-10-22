using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>THE SPIRAL ROOM on this head, through the fuse arming, the fuse rail chip (the way in
/// since the Spiral row left the rail, WPF MainWindow.SpiralRoom.cs:76) and the fuse's kill switch -
/// SpiralTabView.Refresh reading Core SpiralRoom.StateFor.</summary>
public sealed class SpiralRoomShellTests
{
    [Fact]
    public Task FuseArmsTheRailChip_ChipOpensTheFog_KillSwitchEndsBoth() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var (oldAt, oldDone, oldChoice) = (s.DescentCeremonyAtUtc, s.DescentMigrationCompleted, s.PendingDescentMigrationChoice);
        s.DescentCeremonyAtUtc = null;
        s.DescentMigrationCompleted = false;
        s.PendingDescentMigrationChoice = null;
        var oldFuse = global::ConditioningControlPanel.Avalonia.App.DescentCountdown;
        var fuse = new DescentCountdownService();
        global::ConditioningControlPanel.Avalonia.App.DescentCountdown = fuse;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var chip = shell.Named<global::ConditioningControlPanel.Avalonia.Controls.DescentFuseRailChip>("FuseRailChip")!;
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            Assert.False(chip.IsVisible);   // fuse dark, no block: every install today

            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DescentFusePhase.Vigil, fuse.LastAnnouncedPhase);
            Assert.True(chip.IsVisible);

            chip.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsVisible);
            Assert.True(tab.FindControl<Grid>("FogHost")!.IsVisible);
            Assert.Single(tab.FindControl<Grid>("FogHost")!.Children); // first-light canvas also supplies the fog
            var digits = tab.FindControl<TextBlock>("FogDigits")!;
            Assert.Equal(56, digits.FontSize);   // the hero T-minus, not "any moment now"
            Assert.NotEqual(DescentFuseCopy.FogImminent, digits.Text);
            Assert.False(tab.FindControl<Button>("BtnSpiralHelp")!.IsVisible);   // no "?" over the fog

            fuse.ApplyCeremonyAt(null);   // the kill switch, live
            Dispatcher.UIThread.RunJobs();
            Assert.False(chip.IsVisible);
            Assert.False(tab.FindControl<Grid>("FogHost")!.IsVisible);
            Assert.True(tab.FindControl<Border>("WaitingPanel")!.IsVisible);
        }
        finally
        {
            shell.Close();
            global::ConditioningControlPanel.Avalonia.App.DescentCountdown = oldFuse;
            fuse.Dispose();
            (s.DescentCeremonyAtUtc, s.DescentMigrationCompleted, s.PendingDescentMigrationChoice) = (oldAt, oldDone, oldChoice);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    /// <summary>FIRST LIGHT through the shell's door (WPF MainWindow.BeginSpiralFirstLight -> SpiralTabView.BeginFirstLight): the reveal owns the room,
    /// holds at the hand-off fade with no block, hands back to the waiting room at the 12 s timeout,
    /// and leaving the tab mid-reveal ends it.</summary>
    [Fact]
    public Task FirstLightHoldsForTheBlockThenTimesOutToTheWaitingRoom() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var (oldAt, oldDone, oldMotion) = (s.DescentCeremonyAtUtc, s.DescentMigrationCompleted, s.MotionLevel);
        s.DescentCeremonyAtUtc = null;
        s.DescentMigrationCompleted = false;
        s.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Full;
        var time = new SteppedTime();
        global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = time;
        var oldFuse = global::ConditioningControlPanel.Avalonia.App.DescentCountdown;
        global::ConditioningControlPanel.Avalonia.App.DescentCountdown = null;   // fuse dark: the room is the waiting room

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            var fog = tab.FindControl<Grid>("FogHost")!;
            var waiting = tab.FindControl<Border>("WaitingPanel")!;
            var help = tab.FindControl<Button>("BtnSpiralHelp")!;

            shell.BeginSpiralFirstLight();   // RunFirstLightReveal's landing (no desktop lifetime headless)
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsVisible);
            Assert.True(fog.IsVisible);
            Assert.False(waiting.IsVisible);
            Assert.False(tab.FindControl<StackPanel>("FogCopy")!.IsVisible);
            Assert.False(help.IsVisible);
            var visual = Assert.IsType<global::ConditioningControlPanel.Avalonia.Controls.SpiralFirstLightVisual>(Assert.Single(fog.Children));
            Assert.Equal(0, visual.ElapsedSeconds);

            var holdAt = SpiralFirstLightTimeline.HandoffStartSeconds(false);
            for (var t = 0.5; t <= 11.5; t += 0.5)
            {
                time.Advance(TimeSpan.FromSeconds(0.5));
                global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
                Assert.Equal(Math.Min(t, holdAt), visual.ElapsedSeconds, 3);   // the reveal runs, then holds
            }
            Assert.True(fog.IsVisible);
            Assert.False(waiting.IsVisible);

            time.Advance(TimeSpan.FromSeconds(0.5));   // 12 s: no block came
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            Assert.False(fog.IsVisible);
            Assert.True(waiting.IsVisible);
            Assert.True(help.IsVisible);
            Assert.DoesNotContain(global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Running, l => l.Owner == visual);

            // Leaving the tab mid-reveal ends it; coming back is the ordinary room.
            shell.BeginSpiralFirstLight();
            Assert.True(fog.IsVisible);
            Assert.Contains(global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Running, l => l.Owner == visual);
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Running, l => l.Owner == visual);
            shell.ShowTab(SpiralRoom.TabKey);
            Dispatcher.UIThread.RunJobs();
            Assert.False(fog.IsVisible);
            Assert.True(waiting.IsVisible);
        }
        finally
        {
            shell.Close();
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = TimeProvider.System;
            global::ConditioningControlPanel.Avalonia.App.DescentCountdown = oldFuse;
            (s.DescentCeremonyAtUtc, s.DescentMigrationCompleted, s.MotionLevel) = (oldAt, oldDone, oldMotion);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
