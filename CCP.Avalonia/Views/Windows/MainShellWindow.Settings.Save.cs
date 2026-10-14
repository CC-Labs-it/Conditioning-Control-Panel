using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    /// <summary>WPF Settings.cs:377. Settings are already live; save before asking about presets.
    /// Cancel leaves that save intact, and overwriting keeps the preset's identity and creation date.</summary>
    private async Task SaveSettingsAndOfferPreset()
    {
        CoreSettings.Save();
        var s = CoreSettings.Current;
        var current = Preset.GetDefaultPresets().Concat(s.UserPresets)
            .FirstOrDefault(p => p.Name == s.CurrentPresetName);
        if (current?.IsDefault == true) current = null;
        var choice = await OfferPresetSave(current);
        if (Named<Tabs.PresetsTabView>("PresetsTab") is not { } page) return;
        if (choice == true && current != null)
        {
            if (page.SaveOverPreset(current) is { } updated)
            {
                page.RefreshPresetsList();
                await MessageDialog.ShowAsync(this, Loc.Get("title_preset_saved"), Loc.GetF("msg_preset_0_updated", updated.Name));
            }
        }
        else if (choice == false) await page.PromptSaveNewPreset(this);
    }

    /// <summary>The WPF Yes/No/Cancel offer, using the existing message-box chrome.
    /// True = overwrite; false = save new; null = leave presets alone.</summary>
    private async Task<bool?> OfferPresetSave(Preset? current)
    {
        var message = current == null ? Loc.Get("msg_settings_saved_offer_preset")
            : Loc.GetF("msg_settings_saved_offer_overwrite_format", current.Name);
        var dialog = new MessageDialog(Loc.Get("title_settings_saved"), message, showCancel: true,
            okText: "Yes", cancelText: current == null ? "No" : Loc.Get("btn_cancel"));
        var yes = dialog.FindControl<Button>("BtnOk")!;
        var cancel = dialog.FindControl<Button>("BtnCancel")!;
        var footer = (Grid)yes.Parent!;
        Grid.SetColumn(yes, 1);
        Grid.SetColumn(cancel, 2);
        yes.Margin = new Thickness(0, 0, 10, 0);
        cancel.Margin = default;
        var saveNew = false;
        if (current != null)
        {
            footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(cancel, 3);
            var no = new Button { Name = "BtnNo", Content = new TextBlock { Text = "No" },
                Theme = cancel.Theme, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(no, 2);
            no.Click += (_, _) => { saveNew = true; dialog.Close(false); };
            footer.Children.Add(no);
        }
        var yesChosen = await dialog.ShowDialogSafe<bool?>(this) == true;
        return yesChosen ? current != null : saveNew ? false : null;
    }
}
