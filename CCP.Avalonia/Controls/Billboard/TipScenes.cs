// PORTED from ConditioningControlPanel/Controls/Billboard/TipScenes.cs: one picture per tip.

using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>WPF TipScenes: the tip card carries its tip id as art data; an unknown id keeps the
    /// generic <see cref="TipArtView"/>.</summary>
    public static class TipScenes
    {
        public static Control Create(string? id) => id switch
        {
            "quests" => new QuestsTipArt(),
            "awareness" => new AwarenessTipArt(),
            "programs" => new ProgramsTipArt(),
            "deeper" => new DeeperTipArt(),
            "blink" => new BlinkTipArt(),
            "haptics" => new HapticsTipArt(),
            "folders" => new FoldersTipArt(),
            "lockdown" => new LockdownTipArt(),
            "remote" => new RemoteTipArt(),
            _ => new TipArtView(),
        };
    }
}
