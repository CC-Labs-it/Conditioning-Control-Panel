namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    internal static class WebHostTakeover
    {
        // WPF MainWindow.Browser.cs:981: latch before stopping media, so retries cannot resume it.
        internal const string StopScript = "window._ccpForcedFs = false; window._ccpTakeoverStopped = true;" +
            "document.querySelectorAll('video,audio').forEach(v => { try { v.pause(); } catch (e) {} });" +
            "if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(() => {});";
    }
}
