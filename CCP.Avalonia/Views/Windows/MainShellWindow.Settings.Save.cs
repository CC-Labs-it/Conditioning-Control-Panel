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
                await MessageDialog.ShowAsync(this, Loc.Get("title_preset_saved"), Loc.GetF("msg_preset_0_updated", updated.Name));
        }
        else if (choice == false) await page.PromptSaveNewPreset(this);
    }

    /// <summary>WPF Settings.cs:398 (YesNo) and :413 (YesNoCancel).
    /// True = overwrite; false = save new; null = leave presets alone.</summary>
    private async Task<bool?> OfferPresetSave(Preset? current)
    {
        var title = Loc.Get("title_settings_saved");
        if (current != null)
            return await MessageDialog.AskYesNoCancelAsync(this, title,
                Loc.GetF("msg_settings_saved_offer_overwrite_format", current.Name));
        return await MessageDialog.ConfirmAsync(this, title, Loc.Get("msg_settings_saved_offer_preset"),
            okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no")) ? false : null;
    }
}
