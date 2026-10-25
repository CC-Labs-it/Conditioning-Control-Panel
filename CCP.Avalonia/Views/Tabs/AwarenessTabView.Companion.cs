using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AwarenessTabView
{
    private bool _companionPrivacyHosted;

    // WPF AwarenessTabView.Companion.cs:11-22. Reuse the live zones and their viewmodels.
    private void HostCompanionPrivacy()
    {
        if (!CompanionExperience.IsV2Enabled || _companionPrivacyHosted
            || TopLevel.GetTopLevel(this) is not MainShellWindow owner) return;
        var room = owner.Named<CompanionTabView>("CompanionTab")?.Room;
        if (room?.FindControl<AwarenessPrivacyView>("AwarenessZone") is not { } privacy
            || room.WorkshopParts is not { } shelf) return;
        if (privacy.Parent is Panel previous) previous.Children.Remove(privacy);
        // ponytail: keep the live per-zone VM; no aggregate WPF room VM is needed for hosting.
        CompanionPrivacyHost.Content = privacy;
        CompanionTuningHost.Content = shelf.Awareness;
        CompanionTuningFold.IsVisible = true;
        _companionPrivacyHosted = true;
        privacy.ViewModel?.Sync();
        SyncCompanionPrivacyClocks();
    }

    private void SyncCompanionPrivacyClocks()
    {
        if (CompanionPrivacyHost.Content is not AwarenessPrivacyView privacy) return;
        if (IsEffectivelyVisible) { privacy.StartRefresh(); privacy.SyncCursorBlink(); }
        else { privacy.StopRefresh(); privacy.StopCursorBlink(); privacy.WipeConfirm.Disarm(); }
    }

    internal void StopCompanionPrivacyForPanic()
    {
        if (CompanionPrivacyHost.Content is not AwarenessPrivacyView privacy) return;
        privacy.StopRefresh();
        privacy.StopCursorBlink();
        privacy.WipeConfirm.Disarm();
    }

    internal void RevealCompanionTuning()
    {
        HostCompanionPrivacy();
        CompanionTuningFold.IsExpanded = true;
        CompanionTuningFold.BringIntoView();
    }
}
