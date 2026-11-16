using System;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private int _levelReactionVersion;
    private bool _levelReactionsAttached, _levelReactionSpeaking;

    // WPF AvatarTubeWindow.xaml.cs:310-315 and Reactions.cs:542-568.
    private void AttachCompanionLevels()
    {
        DetachCompanionLevels();
        _levelReactionsAttached = true;
        CompanionCore.LevelUp += OnTubeCompanionLevelUp;
        if (_parentWindow is MainShellWindow shell) shell.EngineStopped += OnLevelReactionEngineStopped;
    }

    private void DetachCompanionLevels()
    {
        _levelReactionsAttached = false;
        Interlocked.Increment(ref _levelReactionVersion);
        CompanionCore.LevelUp -= OnTubeCompanionLevelUp;
        if (_parentWindow is MainShellWindow shell) shell.EngineStopped -= OnLevelReactionEngineStopped;
    }

    private void OnTubeCompanionLevelUp(CompanionId companion, int level)
    {
        int version = Volatile.Read(ref _levelReactionVersion);
        RunOnAvatar(() =>
        {
            if (!_levelReactionsAttached) return;
            UpdateTitleDisplay();
            if (version != Volatile.Read(ref _levelReactionVersion)) return;
            if (PanicAftermath.TriggersSilenced() || (level != CompanionProgress.MaxLevel && level % 10 != 0)) return;
            if (_isPlayingUninterruptibleClip || !IsAvatarVisibleOnScreen || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted) return;
            var name = CompanionDefinition.GetById(companion).Name;
            var modName = CoreMods.IsCCPDefault ? CoreMods.Service?.GetCompanionName() : null;
            name = !string.IsNullOrWhiteSpace(modName) ? modName : CoreMods.MakeModAware(name);
            var line = level == CompanionProgress.MaxLevel
                ? $"{name} reached MAX LEVEL."
                : $"{name} is now level {level}.";
            GigglePriority(line, playSound: false, aiGenerated: false);
            _levelReactionSpeaking = HasBubbleUp;
        });
    }

    // Retire a worker event queued before a stop as well as a line already on screen.
    private void OnLevelReactionEngineStopped(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _levelReactionVersion);
        RunOnAvatar(() => { if (_levelReactionSpeaking) PanicSilence(); });
    }
}
