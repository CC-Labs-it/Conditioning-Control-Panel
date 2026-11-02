using System;
using System.Linq;
using System.Net.WebSockets;

namespace ConditioningControlPanel.Services.Diagnostics
{
    /// <summary>
    /// Unobserved task faults that are not crashes. The Buttplug client closes its Intiface socket
    /// fire-and-forget when the link drops, and a close on a socket that is already Aborted faults
    /// with "The WebSocket is in an invalid state ('Aborted')". Nothing is lost (the socket is
    /// already gone, and the disconnect is handled through ServerDisconnect), but the finalizer
    /// re-throw landed in crash.log as a CRASH REPORT and read as the cause of an unrelated crash
    /// (ccp-bugs #1510, #1513).
    /// </summary>
    internal static class BenignTaskFault
    {
        /// <summary>True when every inner fault is a WebSocket close on a socket that is already
        /// closed or aborted.</summary>
        internal static bool IsLateWebSocketClose(AggregateException? ex)
        {
            if (ex == null) return false;
            var inner = ex.Flatten().InnerExceptions;
            return inner.Count > 0 && inner.All(IsLateClose);
        }

        private static bool IsLateClose(Exception e)
        {
            if (e is not WebSocketException ws || ws.WebSocketErrorCode != WebSocketError.InvalidState) return false;
            var stack = e.StackTrace ?? "";
            return stack.Contains("WebSocket.CloseAsync", StringComparison.Ordinal)
                || stack.Contains("WebSocket.CloseOutputAsync", StringComparison.Ordinal);
        }
    }
}
