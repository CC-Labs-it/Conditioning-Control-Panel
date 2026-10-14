using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/Pages/PersonalityPage.xaml.cs
    /// (5681c132a). Companion > Personality adopts the room's MakeHerYoursView ("PersonalityZone")
    /// and the Workshop Community cell. ponytail: the picker card and the Advanced row's prompt
    /// editor door are companion-v2-pages-c (see the XAML).
    /// </summary>
    public partial class PersonalityPage : UserControl
    {
        public PersonalityPage()
        {
            InitializeComponent();
        }

        /// <summary>WPF PersonalityPage.OnShown, through ShowTab("personality") -> OnTabShown.</summary>
        internal void OnShown(CompanionRoomView? room)
        {
            if (room is null) return;
            if (room.FindControl<MakeHerYoursView>("PersonalityZone") is { } presets)
            {
                // The zone carries its own viewmodel here (WPF re-points DataContext at tab.Vm.Personality).
                CompanionPageHost.Adopt(presets, PresetsHost);
                presets.ViewModel.Sync();   // WPF tab.Vm.Sync -> PersonalityVm.Sync
            }
            if (room.FindControl<WorkshopAccordion>("WorkshopZone")?.DataContext is WorkshopRuntimeVm workshop)
                CompanionPageHost.Adopt(workshop.Parts.Community, CommunityHost);
        }
    }
}
