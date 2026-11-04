using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.ProgramsTab.cs:1864, :1895.
        internal void AnnounceProgramSessionStarted(Session? session)
        {
            var svc = App.Programs;
            if (svc?.ActiveProgram is not { } program || svc.ActiveEnrollment is not { } enrollment
                || !svc.IsProgramSession(session)) return;
            App.Notifications.Show(Loc.GetF("programs_toast_session_started", program.Title, enrollment.CurrentDay),
                NotificationType.Info, TimeSpan.FromSeconds(6));
        }

        internal void AnnounceProgramSessionEnded(Session? session)
        {
            var svc = App.Programs;
            if (svc?.IsProgramSession(session) != true) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (App.Programs != svc || svc.ActiveProgram is not { } program) return;
                var completed = svc.LastProgramSessionCompleted;
                App.Notifications.Show(Loc.GetF(completed ? "programs_toast_session_completed" : "programs_toast_session_ended_early", program.Title),
                    completed ? NotificationType.Success : NotificationType.Warning, TimeSpan.FromSeconds(8),
                    Loc.Get("programs_toast_view_today"), () => ShowTab("programs"));
            }, DispatcherPriority.Background);
        }
    }
}
