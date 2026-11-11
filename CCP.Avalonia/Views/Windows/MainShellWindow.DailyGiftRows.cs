using System;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal static Func<DateTime, string>? GiftSeededKeyFor;
    internal static Func<string, bool>? GiftIsFreeToday;
    internal static Func<DateTime> GiftToday = () => DateTime.Today;

    private void InitializeDailyGiftRows() => Activated += (_, _) => DailyGiftFocus();

    internal void DailyGiftFocus()
    {
        var s = CoreSettings.Current;
        var dirty = NoteDailyGiftUse(s);
        dirty |= MaybePostRelockRow(s);
        if (dirty) CoreSettings.Save();
    }

    internal bool NoteDailyGiftUse(AppSettings s)
    {
        if (GiftSeededKeyFor == null || GiftIsFreeToday == null || CoreEntitlement.HasPremium) return false;
        var today = GiftToday();
        var key = CoreEntitlement.DailyFreeKeyProvider?.Invoke();
        if (GiftSwitchOn(s, key))
            return ConversionRules.Stamp(s.InboxStamps, ConversionRules.KeyGiftUsed, ConversionRules.GiftUse(today, key!));
        var yesterday = GiftSeededKeyFor(today.AddDays(-1));
        if (!GiftSwitchOn(s, yesterday) || GiftIsFreeToday(yesterday)) return false;
        s.InboxStamps.TryGetValue(ConversionRules.KeyGiftUsed, out var last);
        if (last != null && last.StartsWith(ConversionRules.DayStamp(today), StringComparison.Ordinal)) return false;
        return ConversionRules.Stamp(s.InboxStamps, ConversionRules.KeyGiftUsed, ConversionRules.GiftUse(today.AddDays(-1), yesterday));
    }

    private static bool GiftSwitchOn(AppSettings s, string? key) => key switch
    {
        "awareness" => s.KeywordTriggersEnabled || s.AwarenessModeEnabled,
        "takeover" => s.AutonomyModeEnabled || s.MantraChantEnabled,
        "voice" => s.SpokenMantrasEnabled || s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled,
        "haptics" => s.Haptics?.Enabled == true,
        _ => false,
    };

    internal bool MaybePostRelockRow(AppSettings s)
    {
        if (GiftIsFreeToday == null) return false;
        var today = GiftToday();
        s.InboxStamps.TryGetValue(ConversionRules.KeyGiftUsed, out var used);
        if (!ConversionRules.RelockOwed(used, today, CoreEntitlement.HasPremium, GiftIsFreeToday, out var key)) return false;
        const string rowKey = ConversionRules.KeyGiftRelock;
        var stamp = ConversionRules.DayStamp(today);
        if (!ConversionRules.Owed(s.InboxStamps, rowKey, stamp)) return false;
        ConversionRules.Stamp(s.InboxStamps, rowKey, stamp);
        var feature = ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == key);
        var tier = feature?.Tier >= 2 ? 2 : 1;
        var title = Loc.Get(tier >= 2 ? "conv_relock_prime" : "conv_relock_basic");
        var name = feature == null ? "" : Loc.Get(feature.TitleLocKey);
        Platform.StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "conv:" + rowKey, Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.DailyGiftRelockKey, Title = title, Summary = name,
            Open = () => Dialogs.InboxNoticeCard.Present(this, title, name,
                Loc.Get(tier >= 2 ? "conv_get_prime" : "conv_get_basic"),
                () => Platform.ExternalOpener.Open(ConversionRules.PlanUrl(tier))),
        });
        return true;
    }
}
