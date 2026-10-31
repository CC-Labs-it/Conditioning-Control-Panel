using ConditioningControlPanel.Localization;
using Avalonia.Controls;
namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class ModCreatorWindow
    {
        private static readonly (string Key, string Name)[] NavDoorSlots =
        {
            ("nav/door_home.png", "mod_art_slot_nav_door_home"),
            ("nav/door_play.png", "mod_art_slot_nav_door_play"),
            ("nav/door_companion.png", "mod_art_slot_nav_door_companion"),
            ("nav/door_library.png", "mod_art_slot_nav_door_library"),
            ("nav/door_studio.png", "mod_art_slot_nav_door_studio"),
            ("nav/door_you.png", "mod_art_slot_nav_door_you"),
            ("nav/door_webapp.png", "mod_art_slot_nav_door_webapp"),
            ("nav/door_settings.png", "mod_art_slot_nav_door_settings"),
        };
        private static readonly (string Key, string Name)[] PlayWallSlots =
        {
            ("features/dtrh.png", "mod_art_slot_features_dtrh"),
            ("features/loom.png", "mod_art_slot_features_loom"),
            ("features/goon_game.png", "mod_art_slot_features_goon_game"),
            ("features/lab_gaze_hero.png", "mod_art_slot_features_lab_gaze_hero"),
            ("features/lab_focusgaze_hero.png", "mod_art_slot_features_lab_focusgaze_hero"),
            ("features/lab_quiz_hero.png", "mod_art_slot_features_lab_quiz_hero"),
            ("features/lab_aimemory_hero.png", "mod_art_slot_features_lab_aimemory_hero"),
        };
        private static readonly (string Key, string Name)[] VaultDashboardSlots =
        {
            ("features/vault.png", "mod_art_slot_features_vault"),
            ("features/mysterybox.png", "mod_art_slot_features_mysterybox"),
            ("features/justdrop.png", "mod_art_slot_features_justdrop"),
            ("features/fyp.png", "mod_art_slot_features_fyp"),
            ("features/fyp_banner.png", "mod_art_slot_features_fyp_banner"),
            ("features/awareness.png", "mod_art_slot_features_awareness"),
            ("features/remote_control.png", "mod_art_slot_features_remote_control"),
            ("features/blink_trainer.png", "mod_art_slot_features_blink_trainer"),
            ("features/deeper.png", "mod_art_slot_features_deeper"),
            ("lockdown_icon.png", "mod_art_slot_lockdown_icon"),
            ("exclusives/vault_backdrop.png", "mod_art_slot_exclusives_vault_backdrop"),
        };
        private static readonly (string Key, string Name)[] SessionIntakeSlots =
        {
            ("Cards/fireworks.png", "mod_art_slot_cards_fireworks"),
            ("Cards/hearth.png", "mod_art_slot_cards_hearth"),
            ("Cards/spotlight.png", "mod_art_slot_cards_spotlight"),
            ("intake/pass_card.png", "mod_art_slot_intake_pass_card"),
            ("intake/pass_card_bambi.png", "mod_art_slot_intake_pass_card_bambi"),
            ("intake/pass_card_sissy.png", "mod_art_slot_intake_pass_card_sissy"),
            ("intake/pass_card_circe.png", "mod_art_slot_intake_pass_card_circe"),
            ("intake/pass_card_drone.png", "mod_art_slot_intake_pass_card_drone"),
        };
        // WPF ModCreatorWindow.UiArt.cs:39-109, 114-190: slot order and shared image plumbing.
        private void BuildUiArtSection()
        {
            var panel = CreateSectionPanel("uiart");
            var stack = new StackPanel();
            panel.Child = stack;
            stack.Children.Add(CreateSectionHeader(Loc.Get("mod_art_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_art_intro")));
            stack.Children.Add(CreateSubHeader(Loc.Get("mod_art_nav_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_art_nav_help")));
            stack.Children.Add(BuildUiArtSlotWrap(NavDoorSlots, 100, 100));
            stack.Children.Add(CreateSubHeader(Loc.Get("mod_art_play_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_art_play_help")));
            stack.Children.Add(BuildUiArtSlotWrap(PlayWallSlots, 150, 84));
            stack.Children.Add(CreateSubHeader(Loc.Get("mod_art_vault_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_art_vault_help")));
            stack.Children.Add(BuildUiArtSlotWrap(VaultDashboardSlots, 150, 84));
            stack.Children.Add(CreateSubHeader(Loc.Get("mod_art_cards_title")));
            stack.Children.Add(CreateSectionDescription(Loc.Get("mod_art_cards_help")));
            stack.Children.Add(BuildUiArtSlotWrap(SessionIntakeSlots, 110, 110));
        }
        private WrapPanel BuildUiArtSlotWrap((string Key, string Name)[] slots, double width, double height)
        {
            var wrap = new WrapPanel();
            foreach (var (slotKey, name) in slots)
            {
                var displayName = CoreMods.MakeModAware(Loc.Get(name));
                var slot = CreateImageSlot(slotKey, displayName, width, height);
                ToolTip.SetTip(slot, Loc.GetF("mod_art_packed", slotKey));
                wrap.Children.Add(slot);
            }
            return wrap;
        }
    }
}
