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
namespace ConditioningControlPanel.Avalonia.Views.Tabs;
// WPF MainWindow.Enhancements.cs:1938-2007: confirm, purchase, refusal and repaint.
public partial class EnhancementsTabView
{
    private bool _buying;
    internal Func<Window?, string, string, Task<bool>> AskPurchase = (owner, title, message) => ShowPurchaseDialogAsync(owner, title, message, confirm: true);
    internal Func<Window?, string, string, Task> TellFailure = (owner, title, message) => ShowPurchaseDialogAsync(owner, title, message, confirm: false);
    internal SkillPurchase? PurchaseService;
    internal static readonly HashSet<Window> PurchaseDialogs = new();   // open boxes (panic closes them; test hook)
    private static async Task<bool> ShowPurchaseDialogAsync(Window? owner, string title, string message, bool confirm)
    {
        if (owner == null) return false;
        var dialog = new MessageDialog(title, message, confirm,
            okText: confirm ? Loc.Get("btn_yes") : null, cancelText: Loc.Get("btn_no"));
        PurchaseDialogs.Add(dialog);
        try { return await dialog.ShowDialogSafe<bool?>(owner) == true; }
        finally { PurchaseDialogs.Remove(dialog); }
    }
    internal static void ClosePurchaseDialogsForPanic()
    {
        foreach (var dialog in PurchaseDialogs.ToArray()) dialog.Close(false);
    }
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
        bool repaint = false;
        try
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            var pointsLabel = (CoreMods.Service?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower();
            var message = Loc.GetF("msg_purchase_skill", (CoreMods.MakeModAwareProvider?.Invoke(skill.Name) ?? skill.LocalizedName), skill.Cost, pointsLabel,
                              (CoreMods.MakeModAwareProvider?.Invoke(skill.FlavorText) ?? skill.LocalizedFlavorText), (CoreMods.MakeModAwareProvider?.Invoke(skill.Description) ?? skill.LocalizedDescription))
                          + "\n\n" + IconText.BareLoc("msg_skill_permanent_note");   // WPF prefixes a star glyph; no emoji icons on this head (IconGuard)
            if (!await AskPurchase(owner, Loc.Get("dialog_purchase_enhancement"), message)) return;
            card.IsEnabled = false;
            repaint = true;
            var service = PurchaseService ?? SkillPurchase.Current ?? new SkillPurchase();
            var (success, error) = await service.PurchaseSkillAsync(skillId);
            if (!success && !string.IsNullOrEmpty(error)) await TellFailure(owner, Loc.Get("dialog_purchase_failed"), error);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Skill purchase click failed"); }
        finally
        {
            try { if (repaint) { card.IsEnabled = true; Repaint(); } }
            finally { _buying = false; }
        }
    }
}
