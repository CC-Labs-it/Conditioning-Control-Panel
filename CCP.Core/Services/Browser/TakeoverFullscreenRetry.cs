using System.Text.Json;

namespace ConditioningControlPanel.Services.Browser
{
    /// <summary>
    /// When a takeover's fullscreen request was refused for want of a user gesture, the host asks
    /// once more through the DevTools protocol (BUG-HG326JRX2V). Pure rules, so they are testable.
    /// </summary>
    internal static class TakeoverFullscreenRetry
    {
        /// <summary>
        /// Retry only the takeover's own first refusal, only when the page said it had no user
        /// activation, and never a refusal of the re-check itself (its reason starts "recheck").
        /// </summary>
        public static bool ShouldRetryWithGesture(string? message, bool alreadyRetried)
        {
            if (alreadyRetried || string.IsNullOrEmpty(message)) return false;
            if (!message.Contains("\"type\":\"fsDenied\"")) return false;
            if (message.Contains("recheck")) return false;
            return message.Contains("\"activation\":false");
        }

        /// <summary>The <c>Runtime.evaluate</c> parameters: the script, run as if the player clicked.</summary>
        public static string EvaluateParams(string script)
            => JsonSerializer.Serialize(new { expression = script, userGesture = true });

        /// <summary>
        /// ccp-bugs #1699 #1710: Chromium refuses HTML fullscreen from a view without keyboard focus,
        /// gesture or not. The retry raises the panel only when it is on screen, not minimised and
        /// not already the active window; a minimised or hidden panel stays where it is.
        /// </summary>
        public static bool ShouldRaiseHost(bool visible, bool minimized, bool active)
            => visible && !minimized && !active;

        /// <summary>How long the retry waits for focus to reach the browser before the gesture.</summary>
        public const int FocusSettleMs = 150;
    }
}
