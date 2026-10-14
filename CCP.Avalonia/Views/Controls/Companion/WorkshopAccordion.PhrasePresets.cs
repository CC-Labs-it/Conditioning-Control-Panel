using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    public partial class WorkshopAccordion
    {
        private bool _loadingPhrasePresets;

        private void WirePhrasePresets(WorkshopShelfParts parts)
        {
            parts.Triggers.PhrasePresetSelectionChanged += (_, _) =>
            {
                if (_loadingPhrasePresets || parts.Triggers.FindControl<ComboBox>("CmbPhrasePresets")!.SelectedItem is not PhrasePreset preset) return;
                var settings = CoreSettings.Current;
                // A refresh restores the selected ID; it must not reapply an old snapshot over live edits.
                if (settings.CurrentPhrasePresetId == preset.Id) return;
                preset.ApplyToSettings();
                settings.CurrentPhrasePresetId = preset.Id;
                CoreSettings.Save();
            };
            parts.Triggers.SavePhrasePresetRequested += async (_, _) => await SavePhrasePresetAsync();
            parts.Triggers.DeletePhrasePresetRequested += async (_, _) => await DeletePhrasePresetAsync(parts);
            AttachedToVisualTree += (_, _) => RefreshPhrasePresets();
            RefreshPhrasePresets();
        }

        internal void RefreshPhrasePresets()
        {
            if (DataContext is not WorkshopRuntimeVm vm) return;
            var combo = vm.Parts.Triggers.FindControl<ComboBox>("CmbPhrasePresets")!;
            _loadingPhrasePresets = true;
            try
            {
                combo.ItemsSource = null;
                combo.ItemsSource = CoreSettings.Current.PhrasePresets;
                combo.SelectedValue = CoreSettings.Current.CurrentPhrasePresetId;
            }
            finally { _loadingPhrasePresets = false; }
        }

        private async Task SavePhrasePresetAsync()
        {
            if (TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
            var input = new TextBox
            {
                Text = $"Preset {CoreSettings.Current.PhrasePresets.Count + 1}",
                Background = Brush("PanelBgBrush"), Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse(CoreMods.AccentColorHex)),
                Padding = new Thickness(8, 5), Margin = new Thickness(0, 0, 0, 15),
            };
            var dialog = new Window
            {
                Title = Loc.Get("title_save_phrase_preset"), Width = 350, Height = 150,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("DarkerBgBrush"),
            };
            var grid = new Grid { Margin = new Thickness(15), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
            var label = new TextBlock { Text = "Enter a name for this phrase preset:", Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8) };
            grid.Children.Add(label); Grid.SetRow(input, 1); grid.Children.Add(input);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "Save", Width = 80, Padding = new Thickness(8, 5),
                Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.White, BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Color.Parse(CoreMods.AccentColorHex)) };
            var cancel = new Button { Content = "Cancel", Width = 80, Padding = new Thickness(8, 5),
                Foreground = Brushes.White, BorderThickness = new Thickness(0), Background = Brush("PanelAccentBrush") };
            save.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(save); buttons.Children.Add(cancel); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
            dialog.Content = grid;
            dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
            if (!await dialog.ShowDialogSafe<bool>(owner) || string.IsNullOrWhiteSpace(input.Text)) return;
            // ponytail: the live CompanionPhraseService has not crossed. WPF's null-service count is 0;
            // preserve that truthful fallback until the active phrase pool crosses in the -b lane.
            var preset = PhrasePreset.FromCurrentSettings(input.Text.Trim(), activePhraseCount: 0);
            CoreSettings.Current.PhrasePresets.Add(preset);
            CoreSettings.Current.CurrentPhrasePresetId = preset.Id;
            CoreSettings.Save();
            RefreshPhrasePresets();
        }

        private async Task DeletePhrasePresetAsync(WorkshopShelfParts parts)
        {
            if (TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
            if (parts.Triggers.FindControl<ComboBox>("CmbPhrasePresets")!.SelectedItem is not PhrasePreset preset)
            {
                await MessageDialog.ShowAsync(owner, "No Preset Selected", Loc.Get("msg_please_select_a_preset_to_delete"));
                return;
            }
            if (!await MessageDialog.ConfirmAsync(owner, "Delete Preset",
                $"Delete phrase preset '{preset.Name}'?\n\nThis cannot be undone.", okText: "Yes", cancelText: "No")) return;
            CoreSettings.Current.PhrasePresets.Remove(preset);
            CoreSettings.Current.CurrentPhrasePresetId = null;
            CoreSettings.Save();
            RefreshPhrasePresets();
        }

        private IBrush Brush(string key) => this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush : new SolidColorBrush(Color.Parse("#1C1C1C"));
    }
}
