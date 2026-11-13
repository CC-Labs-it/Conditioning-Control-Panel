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

    private void InitializeDailyGiftRows()
    {
        Activated += (_, _) => DailyGiftFocus();
        // WPF MaybePostConversionRows also runs after a server reading: the focus read's member flag lands late.
        void Extras() { if (IsVisible) DailyGiftFocus(); }
        Platform.AccountSeed.ProfileExtrasApplied += Extras;
        Closed += (_, _) => Platform.AccountSeed.ProfileExtrasApplied -= Extras;
    }

    internal void DailyGiftFocus()
    {
        var s = CoreSettings.Current;
        var dirty = NoteDailyGiftUse(s);
        dirty |= MaybePostRelockRow(s);
        dirty |= MaybePostMemberHello(s);
        if (dirty) CoreSettings.Save();
    }

    /// <summary>WPF Patreon IsFreeMember: a Patreon free member (server patreon_member) with no paid access.</summary>
    internal static bool IsFreeMember => CoreSettings.Current.PatreonMember && !CoreAccount.HasPremiumAccess;

    /// <summary>WPF MainWindow.Inbox.cs MaybePostMemberHello: once ever, for a Patreon free member with no tier
    /// (proposal 6b / 6d): thanks, and the second Daily Gift they now get. True when a stamp changed.</summary>
    internal bool MaybePostMemberHello(AppSettings s)
    {
        const string rowKey = ConversionRules.KeyFreeMemberHello;
        if (!IsFreeMember || !ConversionRules.Owed(s.InboxStamps, rowKey, "1")) return false;
        ConversionRules.Stamp(s.InboxStamps, rowKey, "1");
        var title = Loc.Get("conv_member_hello_title");
        var summary = Loc.Get("conv_member_hello_body");
        Platform.StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "conv:" + rowKey, IconKey = "Gift", Title = title, Summary = summary,
            Open = () => Dialogs.InboxNoticeCard.Present(this, title, MemberHelloBody(summary), null, null, Loc.Get("conv_ok")),
        });
        return true;
    }

    /// <summary>Named at open time: the second gift moves with the day, and a row can wait in the Inbox.</summary>
    internal static string MemberHelloBody(string summary)
    {
        var second = App.DailyFree?.MemberKey;
        var feature = second == null ? null : ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == second);
        return feature == null ? summary : summary + " " + Loc.GetF("conv_member_second", Loc.Get(feature.TitleLocKey));
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
