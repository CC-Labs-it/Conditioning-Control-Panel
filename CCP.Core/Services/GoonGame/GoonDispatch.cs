using System;
using Serilog;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Marshals Goon Game callbacks onto the head's UI thread through <see cref="CoreDispatch"/>.
    ///
    /// Every transport raises its events from somewhere else (a SIPSorcery SCTP thread, an
    /// HttpClient continuation, a Task.Delay timer) and the match engine and HUD both touch UI
    /// objects, so nothing may surface on those threads. With no head attached (tests, a console
    /// harness) <see cref="CoreDispatch.Post"/> runs inline, which keeps ordering and is what
    /// makes the loopback transport usable from a plain test harness. A callback that throws is
    /// logged, never allowed to reach the dispatcher's unhandled-exception path.
    /// </summary>
    internal static class GoonDispatch
    {
        public static void Post(Action action)
        {
            if (action == null) return;
            CoreDispatch.Post(() =>
            {
                try { action(); }
                catch (Exception ex) { Log.Error(ex, "[Goon] Dispatched callback failed"); }
            });
        }
    }
}
