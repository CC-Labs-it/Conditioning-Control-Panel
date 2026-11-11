// PORTED from ConditioningControlPanel/Views/Tabs/AwarenessTabView.Companion.cs: under Companion v2 the
// room is collapsed, so its privacy card (Z5, "AwarenessZone") and the Workshop Awareness cell live here.

using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AwarenessTabView
    {
        private bool _companionPrivacyHosted;

        /// <summary>WPF HostCompanionPrivacy (Loaded / IsVisibleChanged there; ShowTab("awareness") here).</summary>
        internal void HostCompanionPrivacy(CompanionRoomView? room)
        {
            if (_companionPrivacyHosted || room?.FindControl<AwarenessPrivacyView>("AwarenessZone") is not { } privacy) return;
            CompanionPageHost.Adopt(privacy, this.FindControl<ContentControl>("CompanionPrivacyHost")!);
            if (room.FindControl<WorkshopAccordion>("WorkshopZone")?.DataContext is WorkshopRuntimeVm workshop)
                CompanionPageHost.Adopt(workshop.Parts.Awareness, this.FindControl<ContentControl>("CompanionTuningHost")!);
            this.FindControl<Expander>("CompanionTuningFold")!.IsVisible = true;
            _companionPrivacyHosted = true;
            privacy.ViewModel?.Sync();   // WPF vm.AwarenessVm.Sync()
        }

        // WPF AwarenessPrivacyView.xaml.cs:36-39,91-100 keeps a visible readout refreshing.
        // Panic only stops its blink and disarms wipe (stricter than WPF unload:49-53).
        internal void StopCompanionPrivacyForPanic()
        {
            if (this.FindControl<ContentControl>("CompanionPrivacyHost")?.Content is not AwarenessPrivacyView privacy) return;
            privacy.StopCursorBlink();
            privacy.WipeConfirm.Disarm();
        }

        /// <summary>WPF RevealCompanionTuning: the room's "tune" link lands on the open fold.</summary>
        internal void RevealCompanionTuning(CompanionRoomView? room)
        {
            HostCompanionPrivacy(room);
            var fold = this.FindControl<Expander>("CompanionTuningFold")!;
            fold.IsExpanded = true;
            fold.BringIntoView();
        }
    }
}
