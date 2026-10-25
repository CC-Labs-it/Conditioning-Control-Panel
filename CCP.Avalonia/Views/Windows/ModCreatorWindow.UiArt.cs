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
        private void BuildUiArtSection()
        {
            var panel = CreateSectionPanel("uiart");
            var stack = new StackPanel();
            panel.Child = stack;
            stack.Children.Add(CreateSectionHeader("UI Art"));
            stack.Children.Add(CreateSectionDescription("The rest of the app's artwork: the nav rail doors, the Play wall heroes, the vault and dashboard tiles, and the session and intake cards. Every slot is a straight file swap -- your image is packed as resources/<the filename under each slot>. Leave a slot empty and the built-in art stays."));
            stack.Children.Add(CreateSubHeader("Nav Doors"));
            stack.Children.Add(CreateSectionDescription("The seven room icons down the left rail. Square transparent PNGs -- 64x64 is the shipped size, 128x128 supplies a crisper HiDPI version. Anything opaque or non-square will letterbox inside the rail button."));
            stack.Children.Add(BuildUiArtSlotWrap(NavDoorSlots, 100, 100));
            stack.Children.Add(CreateSubHeader("Play Wall"));
            stack.Children.Add(CreateSectionDescription("Hero and card art on the Play door. Wide 16:9 (1376x768) reads best. Shared filenames repaint every surface that uses them."));
            stack.Children.Add(BuildUiArtSlotWrap(PlayWallSlots, 150, 84));
            stack.Children.Add(CreateSubHeader("Vault & Dashboard"));
            stack.Children.Add(CreateSectionDescription("Dashboard tiles, Velvet Vault faces and the premium quick-launch rail chips. Wide 16:9 (1376x768) unless the slot says otherwise. Compose with the subject near the middle for cropped surfaces."));
            stack.Children.Add(BuildUiArtSlotWrap(VaultDashboardSlots, 150, 84));
            stack.Children.Add(CreateSubHeader("Session & Intake Cards"));
            stack.Children.Add(CreateSectionDescription("The three session-complete celebration cards (one is picked at random when a session ends) and the Intake weekly pass cards. Squares. \"Pass card, all niches\" is the override that wins for every niche at once -- fill it and the four per-niche cards below are never reached; leave it empty to theme each niche separately."));
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
