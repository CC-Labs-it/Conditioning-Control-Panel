namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// One physical press can reach the app twice on Linux: the XInput2 listener and the GlobalShortcuts
    /// portal both hear it when an X11 window has focus. Counted twice, that single press stops
    /// everything and then exits the app through the double-press rung. This admits the first report
    /// of a press and drops the other route's echo. The same route is never dropped, so a real double
    /// press still exits, and the first report of a press is never dropped, so panic is never delayed.
    /// </summary>
    internal sealed class PanicRouteGate
    {
        internal enum Route { X11, Portal }

        /// <summary>Both reports landed in the same millisecond on Hyprland. A person cannot press
        /// twice this fast, and losing one would only mean pressing again.</summary>
        internal const int EchoWindowMs = 150;

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
