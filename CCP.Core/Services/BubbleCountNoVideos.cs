namespace ConditioningControlPanel.Services;

/// <summary>
/// #1449: with no video in the assets folder and no active pack, Bubble Count used to log
/// "No videos found", hand its queue slot back and stop - nothing on screen, the switch still
/// on, and the player filed it as "the bubble counting function is not working". The game
/// plays a local file (BubbleCountWindow opens it by path), so it cannot borrow the online
/// pool; the fix is to say why, in one toast.
/// </summary>
public static class BubbleCountNoVideos
{
    /// <summary>The play-it-now button (and voice / remote "play now") always gets the line.
    /// A scheduled round says it once per launch: the scheduler fires up to ten times an
    /// hour and the reporter's log shows two empty rounds one second apart.</summary>
    public static bool ShouldTell(bool forceTest, bool alreadyToldThisLaunch)
        => forceTest || !alreadyToldThisLaunch;

    /// <summary>Packs installed but every one switched off gets its own line: "add videos"
    /// is the wrong advice when the videos are one switch away.</summary>
    public static string MessageKey(int installedPacks, int activePacks)
        => installedPacks > 0 && activePacks == 0
            ? "bubblecount_no_videos_packs_off"
            : "bubblecount_no_videos";
}
