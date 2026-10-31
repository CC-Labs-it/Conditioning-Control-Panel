namespace ConditioningControlPanel.Services.Video;

/// <summary>WPF GentleWindowRules.PlaybackRate: videos retain their fades but play at normal speed.
/// GentleSpeed applies to GIFs and spirals only.</summary>
internal static class GentleWindowRules
{
    public static float PlaybackRate(bool on, double speed) => 1f;
}
