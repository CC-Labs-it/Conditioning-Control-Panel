using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    // WPF MainWindow.Enhancements.cs:1938-2003: confirm, disable, buy, report, repaint.
    public partial class EnhancementsTabView
    {
        private bool _buying;
        private static readonly HashSet<MessageDialog> PurchaseDialogs = new();
        internal Func<Window?, string, string, Task<bool>> AskPurchase = (owner, title, message) =>
            ShowPurchaseDialog(owner, title, message, true);
        internal Func<Window?, string, string, Task> TellFailure = (owner, title, message) =>
            ShowPurchaseDialog(owner, title, message, false);
        private static async Task<bool> ShowPurchaseDialog(Window? owner, string title, string message, bool question)
        {
            if (owner == null) return false;
            var dialog = new MessageDialog(title, message, question,
                okText: question ? Loc.Get("btn_yes") : null, cancelText: question ? Loc.Get("btn_no") : null);
            PurchaseDialogs.Add(dialog);
            try { return await dialog.ShowDialogSafe<bool?>(owner) == true; }
            finally { PurchaseDialogs.Remove(dialog); }
        }
        internal static void ClosePurchaseDialogsForPanic()
        {
            foreach (var dialog in PurchaseDialogs.ToArray()) dialog.Close(false);
        }
        internal SkillPurchase? PurchaseService;
        private void MakePurchasable(Control card, string skillId)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                e.Handled = true;
                _ = PurchaseAsync(card, skillId);
            };
        }
        internal async Task PurchaseAsync(Control card, string skillId)
        {
            if (_buying) return;
            var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
            if (skill == null) return;
            _buying = true;
            try
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                var pointsLabel = (CoreMods.Service?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower();
                var message = Loc.GetF("msg_purchase_skill", CoreMods.MakeModAware(skill.LocalizedName), skill.Cost, pointsLabel,
                                  CoreMods.MakeModAware(skill.LocalizedFlavorText), CoreMods.MakeModAware(skill.LocalizedDescription))
                              + "\n\n" + IconText.BareLoc("msg_skill_permanent_note");   // the leading star is an icon (IconGuard)
                if (!await AskPurchase(owner, Loc.Get("dialog_purchase_enhancement"), message)) return;
                card.IsEnabled = false;
                try
                {
                    var service = PurchaseService ?? SkillPurchase.Current ?? new SkillPurchase();
                    var (success, error) = await service.PurchaseSkillAsync(skillId);
                    if (!success && !string.IsNullOrEmpty(error))
                        await TellFailure(owner, Loc.Get("dialog_purchase_failed"), error);
                }
                finally
                {
                    card.IsEnabled = true;
                    Repaint();
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Skill purchase click failed"); }
            finally { _buying = false; }
        }
    }
}
