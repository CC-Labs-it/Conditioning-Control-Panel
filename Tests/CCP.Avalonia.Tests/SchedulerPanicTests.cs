using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SchedulerPanicTests
{
    [Fact]
    public void PanicRegistryMarksIdleSchedulerBeforeItCanStart() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            var now = new DateTime(2026, 10, 26, 17, 0, 0);
            shell.SchedulerNow = () => now;
            var s = CoreSettings.Current;
            s.SchedulerEnabled = s.SchedulerMonday = s.SchedulerTuesday = true;
            s.SchedulerStartTime = "16"; s.SchedulerEndTime = "22";
            // Exercise the actual registry hook; an auto-started run would hide its absence.
            PanicSurfaces.All.Single(x => x.Id == "scheduler").Stop(shell);
            Assert.True(shell.Scheduler.ManuallyStoppedDuringSchedule);
            Assert.Equal(SchedulerRun.Action.None, shell.Scheduler.CheckOnStartup(s, now));
            PanicAftermath.ArmQuiet();
            shell.Scheduler.NoteManualStart(); // prove the base's hold independently wins
            shell.TickEngineScheduler();
            Assert.False(CoreEngine.IsRunning);
            now = now.AddDays(1);
            shell.TickEngineScheduler();
            Assert.False(CoreEngine.IsRunning);
        }
        finally { shell.Close(); }
    });
}
