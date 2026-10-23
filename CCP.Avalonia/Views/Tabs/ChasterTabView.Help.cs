// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.Help.cs: the "How it works" card, the
// line over the menu, Reset times and the red flash line. Stakes (:52) lives in ChasterTabView.Keys.cs.
using System.Linq;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private void BtnHowItWorks_Click(object? sender, RoutedEventArgs e) =>
            HowItWorksCard.IsVisible = !HowItWorksCard.IsVisible;

        /// <summary>The line over the menu, the reset link and the red flash line.</summary>
        internal void PaintMenuHelp()
        {
            var open = FiguresEditable();
            TxtMenuHint.Text = Loc.Get(open ? "chaster_menu_hint_edit" : "chaster_menu_hint_locked");
            var edited = CoreSettings.Current.ChasterPriceOverrides is { Count: > 0 } o
                         && o.Keys.Any(id => TabPriceEdit.IsEdited(id, o));
            BtnResetPrices.IsVisible = open && edited;

            // A red flash books Natasha's row, so it follows that row's figure and needs it on.
            var natashaOn = CoreSettings.Current.ChasterPrices?.Contains(NatashasFavourite.EventId) == true;
            TxtFlashDodgeSub.Text = natashaOn
                ? Loc.GetF("chaster_flash_dodge_sub", NatashasFavourite.DodgeMs / 1000,
                    CircesTab.Format(ShownSeconds(NatashasFavourite.EventId), signed: false))
                : Loc.Get("chaster_flash_dodge_needs");
            FlashDodgeRow.Opacity = natashaOn ? 1 : 0.7;
        }

        /// <summary>Every edited time back to the table's figure. Only while no lock runs.</summary>
        private void BtnResetPrices_Click(object? sender, RoutedEventArgs e)
        {
            if (!FiguresEditable()) return;
            if (_editingId != null) CancelPriceEdit();
            CoreSettings.Current.ChasterPriceOverrides = new();
            CoreSettings.Save();
            Log.Information("[Chaster] prices reset to the table by the player");
            PaintStamps();
            if (_trailerShown) OpenTrailer();
        }
    }
}
