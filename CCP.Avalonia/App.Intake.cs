using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    // WPF MainWindow.Presets.cs:1653/1861: early stop earns nothing below half;
    // natural completion always redeems, even when there was no last progress tick.
    internal static void WireIntakeSessionCredit(SessionRunner runner)
    {
        runner.Ticked += () => IntakePunchCard?.NotifySessionProgress(runner.CurrentSession, runner.ProgressPercent);
        runner.Stopped += (session, completed) =>
        {
            if (completed) IntakePunchCard?.NotifySessionCompleted(session);
        };
    }
}
