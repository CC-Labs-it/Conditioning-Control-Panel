namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// One physical press can reach the app twice on Linux: the XInput2 listener and the GlobalShortcuts
    /// portal both hear it when an X11 window has focus. Counted twice, that single press stops
    /// everything and then exits the app through the double-press rung. This admits the first report
    /// of a press and drops the other route's echo. The same route is never dropped, so a real double
    /// press still exits, and the first report of a press is never dropped, so panic is never delayed.
    /// A dropped report still counts for the route's own bookkeeping: the caller decides what that is.
    /// </summary>
    internal sealed class PanicRouteGate
    {
        internal enum Route { X11, Portal }

        /// <summary>Hyprland reports both within 1 ms; KWin consumes the key, so only the portal reports.
        /// The margin is for a loaded desktop, where a late echo would exit the app. It costs little: only a
        /// press heard solely by the other route can be lost, and pressing again recovers it.</summary>
        internal const int EchoWindowMs = 500;

        private readonly object _gate = new();
        private Route? _lastRoute;
        private long _lastMs;

        /// <summary>Thread-safe: each route reports from its own listener thread.</summary>
        internal bool Admit(Route route, long nowMs)
        {
            lock (_gate)
            {
                if (_lastRoute is { } last && last != route && nowMs - _lastMs < EchoWindowMs) return false;
                _lastRoute = route;
                _lastMs = nowMs;
                return true;
            }
        }
    }
}
