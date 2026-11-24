using System;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private AchievementEngine? _reactionAchievements;
    internal Action<string>? AchievementReactionSink; // test seam

    internal void AttachServiceReactions(AchievementEngine? achievements)
    {
        DetachServiceReactions();
        _reactionAchievements = achievements;
        if (achievements != null) achievements.Unlocked += OnTubeAchievementUnlocked;
    }

    private void DetachServiceReactions()
    {
        if (_reactionAchievements != null) _reactionAchievements.Unlocked -= OnTubeAchievementUnlocked;
        _reactionAchievements = null;
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
