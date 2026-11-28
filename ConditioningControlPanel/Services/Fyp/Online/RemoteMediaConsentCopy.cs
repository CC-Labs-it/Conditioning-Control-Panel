namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// The words of the one consent question every picture door asks before anything online is fetched:
/// the Library tab (<c>MainWindow.Assets</c>), the launcher media dialog, Racing Thoughts 3D and Terms &amp;
/// Conditioning. One copy, so the doors never drift apart again (all four said "Reddit" long after the
/// fetch moved to Scrolller).
///
/// <para>What it has to stay true to: the media comes from Scrolller (<see cref="ScrolllerSource"/>,
/// fetched by this machine, never through our servers; our server only answers the on/off switch,
/// <see cref="ScrolllerPolicyService"/>). Fetched files are kept on this machine for a short while
/// (<see cref="ScrolllerDiskCache"/>, swept at launch and hourly by <see cref="ScrolllerCopySweeper"/>),
/// so the text must not say nothing is saved. "Subreddits" stays: it is what the Library tab calls the
/// communities a player picks or types in.</para>
///
/// <para>A language file entry under <see cref="TitleKey"/> / <see cref="MessageKey"/> still wins over
/// these English words; none exists today.</para>
/// </summary>
internal static class RemoteMediaConsentCopy
{
    public const string TitleKey = "title_remote_media_consent";
    public const string MessageKey = "msg_remote_media_consent";

    public const string Title = "Use Scrolller media?";

    public const string Message =
        "Pull media from Scrolller?\n\n" +
        "The app will stream images and clips from the subreddits you pick, straight from your own machine. " +
        "Copies stay briefly on your own disk, nothing is uploaded, and none of it goes through our servers.\n\n" +
        "It is adult content and it is not curated by us - you choose the niches and subreddits, and only those are ever fetched.\n\n" +
        "Turn it on?";
}
