using System;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class PresetsTabView
{
    private void OnModLabelsAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        CoreMods.ModChanged += OnModLabelsChanged;
        LocalizationManager.Instance.LanguageChanged += OnModLabelsLanguageChanged;
        PaintModLabels();
    }

    private void OnModLabelsDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        CoreMods.ModChanged -= OnModLabelsChanged;
        LocalizationManager.Instance.LanguageChanged -= OnModLabelsLanguageChanged;
    }

    private void OnModLabelsChanged(object? sender, ModPackage mod) => QueueModLabels();
    private void OnModLabelsLanguageChanged(object? sender, EventArgs e) => QueueModLabels();
    private void QueueModLabels() => Dispatcher.UIThread.Post(() =>
    {
        if (VisualRoot is not null) PaintModLabels();
    });

    // WPF MainWindow.UiUpdates.cs:145-152. Keep the full icon prefix for IconLabel.
    private void PaintModLabels()
    {
        SetModLabel(TxtFeatureCornerGif, "🖼 Corner GIF", "label_corner_gif");
        SetModLabel(TxtDetailFlashLabel, "⚡ Flash Images", "section_flash_images");
        SetModLabel(TxtDetailVideoLabel, "🎬 Mandatory Videos", "label_mandatory_videos");
        SetModLabel(TxtDetailSubLabel, "💭 Subliminals", "section_subliminals");
        SetModLabel(TxtSessionFlashLabel, "⚡ Flash Images", "section_flash_images");
        SetModLabel(TxtSessionSubLabel, "💭 Subliminals", "section_subliminals");
    }

    private static void SetModLabel(IconLabel label, string english, string key)
    {
        var mod = CoreMods.MakeModAware(english);
        label.Text = mod != english ? mod : Loc.Get(key);
    }
}
