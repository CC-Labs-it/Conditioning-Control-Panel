// PORTED from WPF MainWindow/MainWindow.UiUpdates.cs ApplyModFeatureNames (the MLCard block, :100-130).
// The mosaic's Title="" literals are English design-time fallbacks; this is the only place they get
// localized AND mod-renamed. Same keys as the Studio rack rows (StudioTabView.LabelFor), so a mod that
// renames "Flash Images" renames the tile, the rack row and the panel title together. Absent on purpose,
// as on WPF: the ? box (RefreshMysteryTile owns it) and the Deeper editor tile ({loc:Str} in the XAML).
using System;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _tileTitlesHooked;

        /// <summary>Paints the tile titles now and on every mod or language change while attached.</summary>
        private void InitTileTitles()
        {
            ApplyTileTitles();
            AttachedToVisualTree += (_, _) =>
            {
                if (_tileTitlesHooked) return;
                _tileTitlesHooked = true;
                CoreMods.ModChanged += OnTileTitlesModChanged;
                LocalizationManager.Instance.LanguageChanged += OnTileTitlesLanguageChanged;
                ApplyTileTitles();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                if (!_tileTitlesHooked) return;
                _tileTitlesHooked = false;
                CoreMods.ModChanged -= OnTileTitlesModChanged;
                LocalizationManager.Instance.LanguageChanged -= OnTileTitlesLanguageChanged;
            };
        }

        // WPF UiUpdates.cs:134: a mod switch also repaints the ? box's reveal art.
        private void OnTileTitlesModChanged(object? sender, ModPackage mod) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { ApplyTileTitles(); RefreshMysteryFace(); });

        private void OnTileTitlesLanguageChanged(object? sender, EventArgs e) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(ApplyTileTitles);

        /// <summary>WPF ApplyModFeatureNames, mosaic part. The Vault's title uses the Exclusives tab's own key.</summary>
        internal void ApplyTileTitles()
        {
            CardFlash.Title = MLCard("Flash Images", "section_flash_images");
            CardSubliminal.Title = MLCard("Subliminals", "section_subliminals_2");
            CardBouncingText.Title = MLCard("Bouncing Text", "label_bouncing_text");
            CardBubblePop.Title = MLCard("Bubble Pop", "label_bubble_pop");
            CardLockCard.Title = MLCard("Lock Card", "label_lock_card");
            CardVault.Title = MLCard("The Vault", "tab_exclusives");
            ComboVideoBubble.TitleA = MLCard("Mandatory Video", "section_mandatory_video");
            ComboVideoBubble.TitleB = MLCard("Bubble Count", "label_bubble_count");
            ComboSpiralPink.TitleA = MLCard("Spiral Overlay", "label_spiral_overlay");
            ComboSpiralPink.TitleB = MLCard("Pink Filter", "label_pink_filter");
            ComboMindDrain.TitleA = MLCard("Mind Wipe", "label_mind_wipe");
            ComboMindDrain.TitleB = MLCard("Brain Drain", "label_brain_drain");
        }

        /// <summary>WPF MLCard = StripLeadingGlyph(ModAwareLabel(english, key)): a mod's own rename wins,
        /// else the localized string, else the English fallback (never a raw key). The shared section
        /// keys carry a leading emoji and the tiles draw their own art, so the glyph is stripped.</summary>
        private static string MLCard(string english, string locKey)
        {
            var modText = CoreMods.MakeModAware(english);
            string text;
            if (!string.IsNullOrEmpty(modText) && !string.Equals(modText, english, StringComparison.Ordinal))
                text = modText;
            else
            {
                text = Loc.Get(locKey);
                if (string.IsNullOrEmpty(text) || string.Equals(text, locKey, StringComparison.Ordinal)) text = english;
            }
            return HoverBubbleBar.StripLeadingGlyph(text);
        }
    }
}
