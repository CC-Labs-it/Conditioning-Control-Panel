using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private void InitializeProfileEquip()
    {
        ProfileEquipSync.Adopted += OnProfileEquipAdopted;
        AccountSeed.ProfileLoaded += OnProfileEquipLoaded;
        var sync = AccountSeed.Sync; if (sync != null) sync.CosmeticsReady += OnProfileEquipLoaded;
        Closed += (_, _) =>
        {
            ProfileEquipSync.Adopted -= OnProfileEquipAdopted;
            AccountSeed.ProfileLoaded -= OnProfileEquipLoaded;
            if (sync != null) sync.CosmeticsReady -= OnProfileEquipLoaded;
        };
        if (AccountSeed.Sync?.Loaded == true) OnProfileEquipLoaded();
    }

    private void OnProfileEquipLoaded() => Dispatcher.UIThread.Post(() => { OnProfileEquipAdopted(); _ = ProfileEquipSync.PullOnceAsync(); });

    private void OnProfileEquipAdopted()
    {
        if (_profileViewingSelf) ApplyOwnProfileWardrobe();
        RefreshProfileBubble();
    }
}
