// WPF MainWindow.ProfileCosmetics: paint every equipped and cleared state. Avatar slot
// ownership and quick pin gestures remain the named follow-up in the parity ledger.
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: retain the existing Core sanitization and ModArt decoders. This painter
        // handles banner/accent/title/pins beside the already wired wardrobe apply path.
        private void ApplyProfileCosmeticsPaint(ProfileCosmetics cosmetics)
        {
            var page = ProfilePage;
            if (page == null) return;
            // WPF wraps each ApplyProfileX in its own try: one failing step must not skip the rest.
            Step("banner", () =>
            {
                if (page.FindControl<Border>("ProfileHeroBanner") is { } banner)
                    banner.Background = ModArt.Banner(cosmetics.BannerId, 1024, AlignmentY.Top);
            });
            Step("accent", () => PaintAccent(page, cosmetics));
            Step("title", () => PaintTitle(page, cosmetics));
            Step("pins", () => PaintPins(page, cosmetics));
        }

        private static void Step(string name, Action paint)
        {
            try { paint(); }
            catch (Exception ex) { Log.Debug("ApplyProfileCosmeticsPaint {Step}: {E}", name, ex.Message); }
        }

        private static void PaintAccent(DiscordTabView page, ProfileCosmetics cosmetics)
        {
            var hasAccent = cosmetics.Accent != null;
            var accent = Color.Parse(cosmetics.Accent ?? "#FF69B4");
            page.ProfileHeroCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B));
            if (page.FindControl<Border>("ProfileAccentGlow") is { } glow)
                glow.BoxShadow = hasAccent
                    ? new BoxShadows(new BoxShadow { Color = Color.FromArgb(0x80, accent.R, accent.G, accent.B), Blur = 22 })
                    : default;
            var headerBrush = new SolidColorBrush(hasAccent ? accent : Colors.White);
            foreach (var name in new[] { "TxtProfileRecordHeader", "TxtProfileShowcaseHeader", "TxtProfileCommunityHeader" })
                if (page.FindControl<TextBlock>(name) is { } header) header.Foreground = headerBrush;
        }

        private static void PaintTitle(DiscordTabView page, ProfileCosmetics cosmetics)
        {
            if (page.FindControl<TextBlock>("TxtProfileEquippedTitle") is { } title)
            {
                title.Text = cosmetics.TitleId != null && Achievement.All.TryGetValue(cosmetics.TitleId, out var achievement)
                    ? CoreMods.MakeModAware(achievement.TitleName) : string.Empty;
                title.IsVisible = !string.IsNullOrEmpty(title.Text);
            }
        }

        private void PaintPins(DiscordTabView page, ProfileCosmetics cosmetics)
        {
            var tiles = new List<ProfileAchievementTile>();
            foreach (var id in cosmetics.PinnedAchievements)
            {
                if (!Achievement.All.TryGetValue(id, out var achievement)) continue;
                var art = ModArt.TryLoad($"achievements/{achievement.ImageName}", 176);
                if (art != null) tiles.Add(new ProfileAchievementTile(id, CoreMods.MakeModAware(achievement.TitleName), art));
            }
            if (page.FindControl<ItemsControl>("ProfilePinnedShowcase") is { } pins)
                pins.ItemsSource = tiles.Count > 0 ? tiles : null;
            if (page.FindControl<StackPanel>("ProfilePinnedPlaceholders") is { } placeholders)
                placeholders.IsVisible = PinPlaceholdersVisible(tiles.Count > 0);
        }
    }
}
