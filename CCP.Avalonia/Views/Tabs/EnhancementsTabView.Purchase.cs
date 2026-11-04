using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class EnhancementsTabView
    {
        private bool _purchasePending;
        private readonly EventBurstLayer _purchaseBurst = new();
        private Border? _prestigeAnchor;
        private readonly SkillPurchase _purchase = new() { IsCurrent = s => ReferenceEquals(s, CoreSettings.Current), SyncBeforeRetry = () => AccountSeed.Sync?.SyncBeforePurchaseRetryAsync() ?? Task.FromResult(false) };
        internal Func<Window?, string, string, Task<bool>> AskPurchase = (owner, title, text) => owner == null ? Task.FromResult(false) : Dialogs.MessageDialog.ConfirmAsync(owner, title, text, okText: Loc.Get("dialog_yes"), cancelText: Loc.Get("dialog_no"));
        internal Func<Window?, string, string, Task> PurchaseError = async (owner, title, text) => { if (owner != null) await Dialogs.MessageDialog.ShowAsync(owner, title, text); };
        internal Func<AppSettings, string, Task<(bool success, string? error, int? points, List<string>? skills, long? lifetimeSpent)>>? PurchaseForTest;

        private void BindPurchase(Control card, SkillDefinition skill)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                _ = PurchaseAsync(card, skill);
            };
        }

        // WPF SkillCard_Click: one prompt or request at a time, and never apply an old account's answer.
        internal async Task PurchaseAsync(Control card, SkillDefinition skill)
        {
            if (_purchasePending || !SkillTreeRules.CanPurchaseSkill(CoreSettings.Current, skill.Id)) return;
            _purchasePending = true;
            var s = CoreSettings.Current;
            var id = s.UnifiedId;
            var owner = TopLevel.GetTopLevel(this) as Window; bool attempted = false;
            try
            {
                var message = Loc.GetF("msg_purchase_skill", CoreMods.MakeModAware(skill.LocalizedName), skill.Cost,
                    (CoreMods.Service?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower(),
                    CoreMods.MakeModAware(skill.LocalizedFlavorText), CoreMods.MakeModAware(skill.LocalizedDescription));
                if (!await AskPurchase(owner, Loc.Get("dialog_purchase_enhancement"), message + "\n\n" + Loc.Get("msg_skill_permanent_note"))) return;
                if (!ReferenceEquals(s, CoreSettings.Current) || s.UnifiedId != id) return;
                card.IsEnabled = false; attempted = true;
                var result = await (PurchaseForTest?.Invoke(s, skill.Id) ?? _purchase.BuyAsync(s, skill.Id));
                if (!ReferenceEquals(s, CoreSettings.Current) || s.UnifiedId != id) return;
                if (!result.success)
                {
                    if (result.points.HasValue) { s.SkillPoints = result.points.Value; CoreSettings.Save(); }
                    if (!string.IsNullOrEmpty(result.error)) await PurchaseError(owner, Loc.Get("dialog_purchase_failed"), result.error);
                    return;
                }
                s.SkillPoints = result.points!.Value;
                s.UnlockedSkills = s.UnlockedSkills.Union(result.skills!).ToList();
                if (skill.Id == "good_girl_streak") { s.StreakShieldsRemaining = 1; s.LastStreakShieldResetDate = DateTime.UtcNow.Date; }
                if (skill.Id == "oopsie_insurance") s.StreakFixCharges++;
                if (skill.Id == "pink_rush") PinkRushHost.Start();
                if (string.IsNullOrEmpty(s.SeasonStatsSeason)) s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, DateTime.UtcNow);
                s.SeasonPointsSpent += skill.Cost;
                long rankBefore = (App.Achievements?.Progress.LifetimeSkillPointsSpent ?? 0) / 100;
                if (App.Achievements is { } achievements)
                {
                    var progress = achievements.Progress;
                    progress.LifetimeSkillPointsSpent = Math.Max(progress.LifetimeSkillPointsSpent + skill.Cost, result.lifetimeSpent ?? 0);
                    if (progress.LifetimeSkillPointsSpent >= AchievementRules.WindowShoppingPointsSpent) achievements.TryUnlock("window_shopping");
                    achievements.Save();
                }
                CoreSettings.Save();
                _purchaseBurst.Fire(card, 100);
                if ((App.Achievements?.Progress.LifetimeSkillPointsSpent ?? 0) / 100 > rankBefore && _prestigeAnchor != null) _purchaseBurst.Fire(_prestigeAnchor, 150);
            }
            finally
            {
                card.IsEnabled = true;
                _purchasePending = false;
                if (attempted) RepaintIfShown();
            }
        }
    }
}
