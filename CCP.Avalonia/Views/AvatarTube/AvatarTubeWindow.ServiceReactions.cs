using System;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private AchievementEngine? _reactionAchievements;
    private MandatoryVideoScheduler? _reactionVideo;
    private bool _videoAnnouncementShowing;
    private int _videoAnnouncementLength;
    private const string VideoAnnouncementAudio = "Ooh! Pretty spir-rals...";
    internal Action<string>? AchievementReactionSink; // test seam

    internal void AttachServiceReactions(AchievementEngine? achievements, MandatoryVideoScheduler? video = null)
    {
        DetachServiceReactions();
        _reactionAchievements = achievements;
        if (achievements != null) achievements.Unlocked += OnTubeAchievementUnlocked;
        _reactionVideo = video;
        if (video != null) { video.VideoAboutToStart += OnTubeVideoAboutToStart; video.VideoForceStopped += OnTubeVideoForceStopped; }
    }

    private void DetachServiceReactions()
    {
        if (_reactionAchievements != null) _reactionAchievements.Unlocked -= OnTubeAchievementUnlocked;
        _reactionAchievements = null;
        if (_reactionVideo != null) { _reactionVideo.VideoAboutToStart -= OnTubeVideoAboutToStart; _reactionVideo.VideoForceStopped -= OnTubeVideoForceStopped; }
        _videoAnnouncementShowing = false;
        _reactionVideo = null;
    }

    // WPF AvatarTubeWindow.Reactions.cs:355: announce the imminent clip.
    private void OnTubeVideoAboutToStart()
    {
        var video = _reactionVideo;
        var generation = video?.PlaybackGeneration;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SpeakVideoAnnouncement(video, generation));
            return;
        }
        SpeakVideoAnnouncement(video, generation);
    }

    private void SpeakVideoAnnouncement(MandatoryVideoScheduler? video, int? generation)
    {
        if (video == null || !ReferenceEquals(video, _reactionVideo) || !video.IsPlaying || video.PlaybackGeneration != generation ||
            Services.Safety.PanicAftermath.TriggersSilenced() || _isPlayingUninterruptibleClip || _isWaitingForAi || _isShowingAiBubble ||
            !IsAvatarVisibleOnScreen || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted) return;
        var line = Loc.Get("avatar_video_announcement");
        GiggleCore(line, false, CompanionEventAudio.Resolve(VideoAnnouncementAudio), false, null, videoAnnouncement: true);
    }

    private void OnTubeVideoForceStopped()
    {
        var video = _reactionVideo;
        var generation = video?.PlaybackGeneration;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => FinishVideoAnnouncement(video, generation)); return; }
        FinishVideoAnnouncement(video, generation);
    }

    private void FinishVideoAnnouncement(MandatoryVideoScheduler? video, int? generation)
    {
        if (video == null || !ReferenceEquals(video, _reactionVideo) || video.PlaybackGeneration != generation) return;
        CancelQueuedVideoAnnouncement();
        if (!_videoAnnouncementShowing) return;
        _videoAnnouncementShowing = false;
        try { _stopSpoken(); } catch { }
        _stopSpoken = static () => { };
        // Listening, thinking, or chat may have taken the bubble without starting new speech.
        if (_isListeningBubble || _isWaitingForAi || _isShowingChatHistory || _isPlayingUninterruptibleClip) return;
        CancelTriggerAnimation();
        _speechTimer?.Stop();
        _speechBubble.IsVisible = false;
        NoteSpeechEnded(SpeechSource.Preset, _videoAnnouncementLength);
    }

    // WPF AvatarTubeWindow.Reactions.cs:525-529. Ignore an event posted before the tube closed.
    private void OnTubeAchievementUnlocked(object? sender, Achievement achievement)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnTubeAchievementUnlocked(sender, achievement));
            return;
        }
        if (!ReferenceEquals(sender, _reactionAchievements) || _reactionAchievements == null) return;
        var line = $"Achievement unlocked: {achievement.Name}! *giggles*";
        if (AchievementReactionSink is { } sink) sink(line);
        else GigglePriority(line, aiGenerated: false);
    }
}
