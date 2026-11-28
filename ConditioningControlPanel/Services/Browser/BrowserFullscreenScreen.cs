using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Browser
{
    /// <summary>
    /// Where a browser video the player fullscreened by hand goes (owner 2026-11-22, ccp-bugs #1720).
    /// Effects follow the content screens ("Show content on"), so a video left on a screen that shows
    /// no content had them drawing elsewhere. When the panel's own screen already shows content
    /// (one of several, or "all monitors"), the video stays there, where the player is looking;
    /// otherwise it goes to the primary content screen, like a takeover. Pure, so it is testable.
    /// </summary>
    internal static class BrowserFullscreenScreen
    {
        public static bool PanelShowsContent(string? panelDevice, IEnumerable<string?> contentDevices)
            => !string.IsNullOrEmpty(panelDevice)
               && contentDevices.Any(d => string.Equals(d, panelDevice, StringComparison.OrdinalIgnoreCase));
    }
}
