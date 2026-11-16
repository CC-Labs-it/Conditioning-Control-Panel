using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private int _levelReactionVersion;
    private bool _levelReactionsAttached;

    // WPF AvatarTubeWindow.xaml.cs:310-315 and Reactions.cs:542-568.
    private void AttachCompanionLevels()
    {
        DetachCompanionLevels();
        _levelReactionsAttached = true;
        CompanionCore.LevelUp += OnTubeCompanionLevelUp;
    }

    private void DetachCompanionLevels()
    {
        _levelReactionsAttached = false;
        Interlocked.Increment(ref _levelReactionVersion);
        CompanionCore.LevelUp -= OnTubeCompanionLevelUp;
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
            var name = CompanionDefinition.GetById(companion).Name;
            var modName = CoreMods.IsCCPDefault ? CoreMods.Service?.GetCompanionName() : null;
            name = !string.IsNullOrWhiteSpace(modName) ? modName : CoreMods.MakeModAware(name);
            var line = level == CompanionProgress.MaxLevel
                ? $"{name} reached MAX LEVEL."
                : $"{name} is now level {level}.";
            GigglePriority(line, aiGenerated: false);   // WPF Reactions.cs:557,561: default playSound giggles
        });
    }
}
