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
    ///
    /// A dispatcher that has begun shutting down drops the callback (a queued BeginInvoke on a
    /// dying dispatcher is a classic shutdown crash in this codebase); the head says so through
    /// <see cref="ShutdownStarted"/>.
    /// </summary>
    internal static class GoonDispatch
    {
        /// <summary>True once the head's UI thread is shutting down. Unseeded = never.</summary>
        public static volatile Func<bool>? ShutdownStarted;

        public static bool ShuttingDown
        {
            get { try { return ShutdownStarted?.Invoke() == true; } catch { return true; } }
        }

        public static void Post(Action action)
        {
            if (action == null || ShuttingDown) return;
            CoreDispatch.Post(() =>
            {
                try { action(); }
                catch (Exception ex) { Log.Error(ex, "[Goon] Dispatched callback failed"); }
            });
        }
    }
}
