using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

/// <summary>WPF MainWindow.ProgramsTab.cs:1864-1940 (called from Presets.cs:2013,2058): the program
/// session's start/end toasts on App.Notifications. A foreign session is silent.</summary>
public partial class MainShellWindow
{
    private void InitializeProgramsToasts()
    {
        Platform.ProgramEngineBridge.ProgramSessionEnded += AnnounceProgramSessionEnded;
        Closed += (_, _) => Platform.ProgramEngineBridge.ProgramSessionEnded -= AnnounceProgramSessionEnded;   // P41
    }

    /// <summary>WPF AnnounceProgramSessionStarted :1864.</summary>
    internal void AnnounceProgramSessionStarted()
    {
        try
        {
            if (App.Programs is not { ActiveProgram: { } program, ActiveEnrollment: { } enrollment } svc) return;
            if (!svc.IsProgramSession(App.Sessions?.CurrentSession)) return;
            App.Notifications.Show(Loc.GetF("programs_toast_session_started", program.Title, enrollment.CurrentDay),
                NotificationType.Info, TimeSpan.FromSeconds(6));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Program session start toast failed"); }
    }

    /// <summary>WPF AnnounceProgramSessionEnded :1895, deferred at Background like WPF. Reads
    /// LastProgramSessionCompleted, not TodayRecord: a boundary-crossing credit lands on yesterday.</summary>
    private void AnnounceProgramSessionEnded() => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            if (App.Programs is not { ActiveProgram: { } program } svc) return;
            var completed = svc.LastProgramSessionCompleted;
            App.Notifications.Show(
                Loc.GetF(completed ? "programs_toast_session_completed" : "programs_toast_session_ended_early", program.Title),
                completed ? NotificationType.Success : NotificationType.Warning,
                TimeSpan.FromSeconds(8), Loc.Get("programs_toast_view_today"), () => ShowTab("programs"));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Program session end toast failed"); }
    }, DispatcherPriority.Background);
}
