namespace ConditioningControlPanel.Services;

/// <summary>The live Home browser pause/resume operation shared with the WPF head.</summary>
public static class BrowserMediaPlaybackScript
{
    public static string ForPaused(bool paused) =>
        "(function(){document.querySelectorAll('audio, video').forEach(el => el." +
        (paused ? "pause" : "play") + "());return '" + (paused ? "paused" : "playing") + "';})();";
}
