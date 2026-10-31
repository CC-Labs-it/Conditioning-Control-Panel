using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class EnhancementsTabView
{
    internal Control? PrestigeAnchor { get; private set; }
    internal Func<Window, string, string, Task<bool>> ConfirmPurchase =
        (owner, title, message) => MessageDialog.ConfirmAsync(owner, title, message);
    internal Func<string, Task<(bool Success, string? Error, long? LifetimeSpent)>> Purchase =
        id => new V2AuthService().PurchaseSkillAsync(id, () => Platform.AccountSeed.Sync?.PushAsync("skill-purchase", true) ?? Task.FromResult(false));
    private bool _purchasing;

    // WPF MainWindow.Enhancements.cs:882,1840,1938-2005. Both ordinary and secret cards
    // use the same confirmation, busy guard and before-rebuild celebration.
    private void WirePurchase(Control card, SkillDefinition skill)
    {
        card.PointerReleased += async (_, e) =>
        {
            if (e.InitialPressMouseButton != global::Avalonia.Input.MouseButton.Left) return;
            e.Handled = true;
            await BuyAsync(card, skill);
        };
    }

    internal async Task BuyAsync(Control card, SkillDefinition skill)
    {
        if (_purchasing || !SkillTreeRules.CanPurchaseSkill(CoreSettings.Current, skill.Id)
            || TopLevel.GetTopLevel(this) is not MainShellWindow owner) return;
        _purchasing = true;
        try
        {
            var mods = App.Mods;
            string message = Loc.GetF("msg_purchase_skill", mods?.MakeModAware(skill.Name) ?? skill.LocalizedName,
                skill.Cost, (mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower(),
                mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText,
                mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription)
                + "\n\n" + Loc.Get("msg_skill_permanent_note");
            if (!await ConfirmPurchase(owner, Loc.Get("dialog_purchase_enhancement"), message)) return;
            card.IsEnabled = false;
            long spentBefore = App.Achievements?.Progress.LifetimeSkillPointsSpent ?? 0;
            var result = await Purchase(skill.Id);
            if (result.Success)
            {
                var s = CoreSettings.Current;
                // WPF SkillTreeService.cs:117-139, ProfileSyncService.cs:4104-4112.
                if (skill.Id == "good_girl_streak") { s.StreakShieldsRemaining = 1; s.LastStreakShieldResetDate = DateTime.UtcNow.Date; }
                if (skill.Id == "pink_rush") PinkRushHost.Start();
                if (skill.Id == "oopsie_insurance") s.StreakFixCharges++;
                if (string.IsNullOrEmpty(s.SeasonStatsSeason)) s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, DateTime.UtcNow);
                s.SeasonPointsSpent += skill.Cost;
                if (App.Achievements is { } achievements)
                {
                    achievements.Progress.LifetimeSkillPointsSpent = Math.Max(spentBefore + skill.Cost, result.LifetimeSpent ?? 0);
                    achievements.IsDirty = true;
                    if (achievements.Progress.LifetimeSkillPointsSpent >= AchievementRules.WindowShoppingPointsSpent)
                        achievements.TryUnlock("window_shopping");
                    achievements.Save();
                }
                CoreSettings.Save();
                owner.CelebrateEnhancementPurchase(card, 1 + spentBefore / 100, PrestigeAnchor);
            }
            else if (!string.IsNullOrEmpty(result.Error))
                await MessageDialog.ShowAsync(owner, Loc.Get("dialog_purchase_failed"), result.Error);
        }
        finally { card.IsEnabled = true; _purchasing = false; Repaint(); }
    }
}
