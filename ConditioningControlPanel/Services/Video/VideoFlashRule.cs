namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// Whether a mandatory video takes the flashes down when it starts (owner, 2026-10-19: a
    /// floating video cleared every flash on screen, and so did a Test press in floating mode).
    ///
    /// The full-screen rule is deliberate and old: a full-screen video covers the screen, so
    /// PlayVideo's prologue stops the flash service ("Stop flashes during video", which also closes
    /// the flashes up) and Cleanup starts it again when the video ends with the engine running and
    /// flashes on. OverlayService pins the compositor host below a playing video for the same
    /// reason (#497). A floating video is a small window among the rest: the flashes stay, the
    /// scheduler keeps going. A Test press follows the same rule as any other trigger.
    /// Pure, pinned by FloatingVideoPolishTests.
    /// </summary>
    internal static class VideoFlashRule
    {
        public static bool StopsFlashes(bool floating) => !floating;
    }
}
