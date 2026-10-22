using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// WPF MainWindow.Enhancements.cs SkillCard_Click: a purchasable node or secret card asks, buys through Core
    /// <see cref="SkillPurchase"/>, bursts on the node (MainWindow.EventFx.cs CelebrateEnhancementPurchase) and, when the
    /// spend crossed a hundred, bursts the Prestige row (CelebratePrestige) before the tree repaints.
    /// </summary>
    public partial class EnhancementsTabView
    {
        private const int EnhancementBurstCount = 100, PrestigeBurstCount = 150;   // WPF EventFx, verbatim

        // Two layers: one layer is one canvas, and the prestige burst lands while the node's is still flying.
        private readonly EventBurstLayer _nodeBurst = new(), _prestigeBurst = new();
        private Control? _prestigeRow;

        /// <summary>Seams (tests): the server client and the two dialogs.</summary>
        internal static Func<V2AuthService> Auth = () => new V2AuthService();
        internal static Func<Window, string, string, Task<bool>> Ask =
            (owner, title, message) => MessageDialog.ConfirmAsync(owner, title, message, okText: Loc.Get("label_yes"));
        internal static Func<Window, string, string, Task> Tell = (owner, title, message) => MessageDialog.ShowAsync(owner, title, message);

        /// <summary>Test hooks: the last purchase started by a click; bursts fired (node, prestige).</summary>
        internal Task? LastPurchase { get; private set; }
        internal (int Node, int Prestige) Bursts => (_nodeBurst.Count, _prestigeBurst.Count);

        /// <summary>WPF: Cursor Hand + MouseLeftButtonUp on purchasable cards only; Enter/Space too (P17).</summary>
        private void MakePurchasable(Control card, string skillId)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.Focusable = true;
            card.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Buy(card, skillId); };
            card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Buy(card, skillId); } };
        }

        private void Buy(Control card, string skillId)
        {
            if (card.IsEnabled) LastPurchase = BuyAsync(card, skillId);
        }

        private async Task BuyAsync(Control card, string skillId)
        {
            var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
            if (skill == null || TopLevel.GetTopLevel(this) is not Window owner) return;
            var points = (App.Mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower();
            var message = Loc.GetF("msg_purchase_skill", CoreMods.MakeModAware(skill.LocalizedName), skill.Cost, points,
                CoreMods.MakeModAware(skill.LocalizedFlavorText), CoreMods.MakeModAware(skill.LocalizedDescription))
                + "\n\n" + IconText.BareLoc("msg_skill_permanent_note");
            if (!await Ask(owner, Loc.Get("dialog_purchase_enhancement"), message)) return;

            card.IsEnabled = false;   // no double purchase while the server answers
            var rankBefore = PrestigeRank();
            try
            {
                var (success, error) = await SkillPurchase.PurchaseAsync(Auth(), skillId, App.Achievements,
                    () => SkillPurchase.SyncBeforeRetryAsync(Platform.AccountSeed.Sync));
                if (success)
                {
                    Log.Information("Skill purchased via UI: {SkillId}", skillId);
                    // Before the repaint below detaches the card: a detached anchor cannot be placed.
                    _nodeBurst.Fire(card, EnhancementBurstCount);
                    if (PrestigeRank() > rankBefore && _prestigeRow != null)
                        _prestigeBurst.Fire(_prestigeRow, PrestigeBurstCount, color: AmbientFxCanvas.Env.GlowColor);
                }
                else if (!string.IsNullOrEmpty(error))
                    await Tell(owner, Loc.Get("dialog_purchase_failed"), error);
            }
            finally
            {
                card.IsEnabled = true;
                Repaint();
            }
        }

        /// <summary>WPF PrestigeRankNow: 1 + lifetime points spent / 100.</summary>
        private static long PrestigeRank() => 1 + (App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0) / 100;
    }
}
