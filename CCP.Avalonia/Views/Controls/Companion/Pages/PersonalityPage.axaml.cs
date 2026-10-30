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
    /// and the Workshop Community cell, with the shared picker card above the presets.
    /// </summary>
    public partial class PersonalityPage : UserControl
    {
        private MakeHerYoursView? _presets;
        private CompanionPickerCard? _picker;

        public PersonalityPage()
        {
            InitializeComponent();
        }

        /// <summary>WPF PersonalityPage.OnShown, through ShowTab("personality") -> OnTabShown.</summary>
        internal void OnShown(CompanionRoomView? room)
        {
            // WPF PersonalityPage.xaml.cs:28-37: retain one picker and refresh on re-entry.
            _picker ??= new CompanionPickerCard
            {
                MorePersonalityOptions = async owner =>
                {
                    if (owner is MainShellWindow shell) await shell.OpenCompanionPromptEditorAsync();
                    _presets?.ViewModel.Sync();
                }
            };
            PickerHost.Content = _picker;
            _picker.Refresh();
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
