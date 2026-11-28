using System;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
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
    private BubbleCountScheduler? _reactionGame;
    private int _gameReactionGeneration;
    internal Action<string>? GameReactionSink;
    private bool _gameReactionShowing;
    private int _gameReactionLength;
    private bool _videoAnnouncementShowing;
    private int _videoAnnouncementLength;
    private const string VideoAnnouncementAudio = "Ooh! Pretty spir-rals...";
    internal Action<string>? AchievementReactionSink; // test seam

    internal void AttachServiceReactions(AchievementEngine? achievements, MandatoryVideoScheduler? video = null, BubbleCountScheduler? game = null)
    {
        DetachServiceReactions();
        _reactionAchievements = achievements;
        if (achievements != null) achievements.Unlocked += OnTubeAchievementUnlocked;
        _reactionGame = game;
        if (game != null) { game.GameCompleted += OnTubeGameCompleted; game.GameFailed += OnTubeGameFailed; game.ForceCleaned += OnTubeGameForceCleaned; }
        _reactionVideo = video;
        if (video != null) { video.VideoAboutToStart += OnTubeVideoAboutToStart; video.VideoForceStopped += OnTubeVideoForceStopped; }
    }

    private void DetachServiceReactions()
    {
        if (_reactionAchievements != null) _reactionAchievements.Unlocked -= OnTubeAchievementUnlocked;
        _reactionAchievements = null;
        Interlocked.Increment(ref _gameReactionGeneration);
        if (_reactionGame != null) { _reactionGame.GameCompleted -= OnTubeGameCompleted; _reactionGame.GameFailed -= OnTubeGameFailed; _reactionGame.ForceCleaned -= OnTubeGameForceCleaned; }
        _reactionGame = null;
        if (_reactionVideo != null) { _reactionVideo.VideoAboutToStart -= OnTubeVideoAboutToStart; _reactionVideo.VideoForceStopped -= OnTubeVideoForceStopped; }
        _videoAnnouncementShowing = false;
        _reactionVideo = null;
    }

    // WPF AvatarTubeWindow.Reactions.cs:384-389,499-503.
    private void OnTubeGameCompleted() => QueueGameReaction(false);
    private void OnTubeGameFailed() => QueueGameReaction(true);

    private void QueueGameReaction(bool failed)
    {
        var game = _reactionGame;
        var generation = Volatile.Read(ref _gameReactionGeneration);
        var roundGeneration = game?.ReactionGeneration;
        void Speak()
        {
            if (game == null || !ReferenceEquals(game, _reactionGame) || roundGeneration != game.ReactionGeneration || generation != Volatile.Read(ref _gameReactionGeneration)
                || Services.Safety.PanicAftermath.TriggersSilenced()) return;
            if (!failed)
            {
                var line = Loc.Get("avatar_bubble_count_win");
                if (GameReactionSink is { } sink) sink(line); else GiggleCore(line, false, null, false, null, videoAnnouncement: false, gameReaction: true);
                return;
            }
            var phrases = CompanionPhraseEditorDialog.LoadPhrases().Where(p => p.Category == "GameFailed" && p.IsEnabled).ToArray();
            if (phrases.Length == 0) return;
            var phrase = phrases[_random.Next(phrases.Length)];
            if (GameReactionSink is { } failureSink) failureSink(phrase.Text);
            else GiggleCore(phrase.Text, false, phrase.AudioFilePath ?? CompanionEventAudio.Resolve(phrase.Text), false, null, videoAnnouncement: false, gameReaction: true);
        }
        if (Dispatcher.UIThread.CheckAccess()) Speak(); else Dispatcher.UIThread.Post(Speak);
    }

    private void OnTubeGameForceCleaned()
    {
        Interlocked.Increment(ref _gameReactionGeneration);
        var game = _reactionGame;
        void Clear()
        {
            if (!ReferenceEquals(game, _reactionGame)) return;
            CancelQueuedGameReaction();
            if (!_gameReactionShowing) return;
            _gameReactionShowing = false;
            try { _stopSpoken(); } catch { }
            _stopSpoken = static () => { };
            if (_isListeningBubble || _isWaitingForAi || _isShowingChatHistory || _isPlayingUninterruptibleClip) return;
            CancelTriggerAnimation();
            _speechTimer?.Stop();
            _speechBubble.IsVisible = false;
            NoteSpeechEnded(SpeechSource.Preset, _gameReactionLength);
        }
        if (Dispatcher.UIThread.CheckAccess()) Clear(); else Dispatcher.UIThread.Post(Clear);
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
