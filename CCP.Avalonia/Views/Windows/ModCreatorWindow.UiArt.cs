using System;
using System.IO;
using Avalonia.Controls;
namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class ModCreatorWindow
    {
        private static readonly (string Key, string Name)[] NavDoorSlots =
        {
            ("nav/door_home.png", "Home door (64px sq)"),
            ("nav/door_play.png", "Play door (64px sq)"),
            ("nav/door_companion.png", "Companion door (64px sq)"),
            ("nav/door_library.png", "Library door (64px sq)"),
            ("nav/door_studio.png", "Studio door (64px sq)"),
            ("nav/door_you.png", "You door (64px sq)"),
            ("nav/door_webapp.png", "Web App door (64px sq)"),
            ("nav/door_settings.png", "Settings door (64px sq)"),
        };
        private static readonly (string Key, string Name)[] PlayWallSlots =
        {
            ("features/dtrh.png", "Rabbit Hole hero (1376x768)"),
            ("features/loom.png", "Loom strip (1376x768)"),
            ("features/goon_game.png", "Goon Game hero (1024 sq)"),
            ("features/lab_gaze_hero.png", "Gaze plate (1376x768)"),
            ("features/lab_focusgaze_hero.png", "Focus Gaze plate (1376x768)"),
            ("features/lab_quiz_hero.png", "Intake plate (1376x768)"),
            ("features/lab_aimemory_hero.png", "AI Memory plate (1376x768)"),
        };
        private static readonly (string Key, string Name)[] VaultDashboardSlots =
        {
            ("features/vault.png", "Velvet Vault (1376x768)"),
            ("features/mysterybox.png", "Mystery Box (1376x768)"),
            ("features/justdrop.png", "JustDrop (1376x768)"),
            ("features/fyp.png", "FYP tile (1376x768)"),
            ("features/fyp_banner.png", "FYP banner (1376x459)"),
            ("features/awareness.png", "Awareness (1376x768)"),
            ("features/remote_control.png", "Remote Control (1376x768)"),
            ("features/blink_trainer.png", "Blink Trainer (1376x768)"),
            ("features/deeper.png", "Deeper (1376x768)"),
            ("lockdown_icon.png", "Lockdown strip (1527x343)"),
            ("exclusives/vault_backdrop.png", "Vault backdrop (wide 16:9)"),
        };
        private static readonly (string Key, string Name)[] SessionIntakeSlots =
        {
            ("Cards/fireworks.png", "Fireworks card (512 sq)"),
            ("Cards/hearth.png", "Hearth card (512 sq)"),
            ("Cards/spotlight.png", "Spotlight card (512 sq)"),
            ("intake/pass_card.png", "Pass card, all niches (1024 sq)"),
            ("intake/pass_card_bambi.png", "Pass card: Bambi (1024 sq)"),
            ("intake/pass_card_sissy.png", "Pass card: Sissy (1024 sq)"),
            ("intake/pass_card_circe.png", "Pass card: Circe (1024 sq)"),
            ("intake/pass_card_drone.png", "Pass card: Drone (1024 sq)"),
        };
        // WPF ModCreatorWindow.UiArt.cs:39-109, 114-190: slot order and shared image plumbing.
        private void BuildUiArtSection()
        {
            var panel = CreateSectionPanel("uiart");
            var stack = new StackPanel();
            panel.Child = stack;
            stack.Children.Add(CreateSectionHeader("UI Art"));
            stack.Children.Add(CreateSectionDescription("Swap artwork for navigation, Play, dashboard and session cards. Empty slots keep the built-in art."));
            stack.Children.Add(CreateSubHeader("Nav Doors"));
            stack.Children.Add(CreateSectionDescription("Eight room icons. Use square transparent PNGs, 64 or 128 pixels wide."));
            stack.Children.Add(BuildUiArtSlotWrap(NavDoorSlots, 100, 100));
            stack.Children.Add(CreateSubHeader("Play Wall"));
            stack.Children.Add(CreateSectionDescription("Play artwork. Filenames remain stable for existing mods. Shared files also change other surfaces."));
            stack.Children.Add(BuildUiArtSlotWrap(PlayWallSlots, 150, 84));
            stack.Children.Add(CreateSubHeader("Vault & Dashboard"));
            stack.Children.Add(CreateSectionDescription("Dashboard and vault artwork. Keep the subject near the middle for cropped tiles."));
            stack.Children.Add(BuildUiArtSlotWrap(VaultDashboardSlots, 150, 84));
            stack.Children.Add(CreateSubHeader("Session & Intake Cards"));
            stack.Children.Add(CreateSectionDescription("Square session and intake cards. The all-niches pass card overrides the four niche cards."));
            stack.Children.Add(BuildUiArtSlotWrap(SessionIntakeSlots, 110, 110));
        }
        private WrapPanel BuildUiArtSlotWrap((string Key, string Name)[] slots, double width, double height)
        {
            var wrap = new WrapPanel();
            foreach (var (slotKey, name) in slots)
            {
                var displayName = CoreMods.MakeModAware(name);
                var slot = CreateImageSlot(slotKey, displayName, width, height);
                ToolTip.SetTip(slot, $"Packed as resources/{slotKey}  (overrides Resources/{slotKey})");
                wrap.Children.Add(slot);
            }
            return wrap;
        }
    }
}
