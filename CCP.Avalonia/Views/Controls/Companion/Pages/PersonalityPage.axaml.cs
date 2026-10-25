using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Serilog;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/Pages/PersonalityPage.xaml.cs
    /// (5681c132a). Companion > Personality adopts the room's MakeHerYoursView ("PersonalityZone")
    /// and the Workshop Community cell. ponytail: the picker card is companion-v2-pages-c (see the XAML).
    /// </summary>
    public partial class PersonalityPage : UserControl
    {
        private MakeHerYoursView? _presets;

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
                _presets = presets;
                CompanionPageHost.Adopt(presets, PresetsHost);
                presets.ViewModel.Sync();   // WPF tab.Vm.Sync -> PersonalityVm.Sync
            }
            if (room.FindControl<WorkshopAccordion>("WorkshopZone")?.DataContext is WorkshopRuntimeVm workshop)
                CompanionPageHost.Adopt(workshop.Parts.Community, CommunityHost);
        }

        /// <summary>WPF OpenPromptEditor_Click: BtnCustomizeCompanion_Click, then re-sync the presets.</summary>
        private async void OpenPromptEditor_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is MainShellWindow shell) await shell.OpenCompanionPromptEditorAsync();
            }
            catch (Exception ex) { Log.Warning(ex, "Companion > Personality: prompt editor failed"); }
            _presets?.ViewModel.Sync();
        }
    }
}
