using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

/// <summary>
/// WPF MainWindow.Inbox.cs:421-541: the two "outside the moment" conversion rows that are FILED, never
/// presented: the companion cap offer (after the conversation tab is gone) and the patron gallery note
/// (after a free unlock, never on the achievement toast). Each is once a local day via InboxStamps.
/// </summary>
public partial class MainShellWindow
{
    // ---- companion cap: filed after the conversation surface is gone (WPF MainWindow.Inbox.cs:421-485)

    /// <summary>Local clock only: ticks every 10 s while a cap row is owed, stops once it files or the
    /// tier makes it moot. No server call.</summary>
    private ITimer? _capWatch;

    /// <summary>The conversation page hit the daily cap. Nothing is shown now; a watch files the row
    /// once Companion is no longer the open tab.</summary>
    internal void OnCompanionCapHit()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnCompanionCapHit); return; }
        try
        {
            if (CoreSettings.Service == null || CapTierNow() >= 2) return;
            var today = ConversionRules.DayStamp(StartupLadder.Time.GetLocalNow().DateTime);
            if (!ConversionRules.Owed(CoreSettings.Current.InboxStamps, ConversionRules.KeyCompanionCap, today)) return;
            _capWatch ??= StartupLadder.Time.CreateTimer(_ =>
            {
                if (Dispatcher.UIThread.CheckAccess()) TryFileCapRow();
                else Dispatcher.UIThread.Post(TryFileCapRow);
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) { Log.Debug("[Inbox] cap watch: {E}", ex.Message); }
    }

    private void TryFileCapRow()
    {
        try
        {
            if (_capWatch == null) return;
            if (string.Equals(CurrentTab, "companion", StringComparison.OrdinalIgnoreCase)
                && IsVisible && WindowState != WindowState.Minimized) return;
            StopCapWatch();
            if (CoreSettings.Service == null || ConversionRules.CapRowOffers(CapTierNow()) is not int tier) return;
            const string rowKey = ConversionRules.KeyCompanionCap;
            var settings = CoreSettings.Current;
            if (!ConversionRules.Stamp(settings.InboxStamps, rowKey, ConversionRules.DayStamp(StartupLadder.Time.GetLocalNow().DateTime))) return;
            CoreSettings.Save();
            var title = tier >= 2
                ? Loc.GetF("conv_cap_basic", ConversionRules.CompanionCapBasic, ConversionRules.CompanionCapPrime)
                : Loc.GetF("conv_cap_free", ConversionRules.CompanionCapFree, ConversionRules.CompanionCapBasic, ConversionRules.CompanionCapPrime);
            var body = Loc.Get("conv_cap_body");
            StartupLadder.Inbox.File(new InboxItem
            {
                Key = "conv:" + rowKey, Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.CompanionCapKey, Title = title, Summary = body,
                Open = () => Dialogs.InboxNoticeCard.Present(this, title, body,
                    Loc.Get(tier >= 2 ? "conv_get_prime" : "conv_get_basic"), () => OpenPlanFromInbox(tier, rowKey)),
            });
        }
        catch (Exception ex) { Log.Debug("[Inbox] cap row: {E}", ex.Message); }
    }

    private void StopCapWatch()
    {
        _capWatch?.Dispose();
        _capWatch = null;
    }

    private static int CapTierNow() => CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess ? 1 : 0;

    // ---- patron gallery (WPF MainWindow.Inbox.cs:487-521)

    /// <summary>A free account unlocked an achievement: one quiet row (never on the toast) saying the
    /// patron gallery holds more. Filed, never presented; once a day. Safe from any thread.</summary>
    internal void PostPatronGalleryRow(Models.Achievement achievement)
    {
        try
        {
            if (achievement == null || achievement.IsExclusive || CoreAccount.HasPremiumAccess || CoreSettings.Service == null) return;
            var settings = CoreSettings.Current;
            var more = Models.Achievement.All.Values.Count(a => a.IsExclusive && !a.IsHidden);
            if (more <= 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                const string rowKey = ConversionRules.KeyPatronGallery;
                if (!ConversionRules.Stamp(settings.InboxStamps, rowKey, ConversionRules.DayStamp(StartupLadder.Time.GetLocalNow().DateTime))) return;
                CoreSettings.Save();
                var title = Loc.GetF("conv_gallery_title", more);
                var body = Loc.Get("conv_gallery_body");
                StartupLadder.Inbox.File(new InboxItem
                {
                    Key = "conv:" + rowKey, Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.PatronGalleryKey, Title = title, Summary = body,
                    Open = () => Dialogs.InboxNoticeCard.Present(this, title, body, Loc.Get("conv_get_basic"), () => OpenPlanFromInbox(1, rowKey)),
                });
            });
        }
        catch (Exception ex) { Log.Debug("[Inbox] gallery row: {E}", ex.Message); }
    }

    /// <summary>Test seam for the browser half (a sandboxed test run refuses non-loopback urls).</summary>
    internal static Func<string, bool> PlanOpener = ExternalOpener.Open;

    /// <summary>WPF MainWindow.Inbox.cs:535-541: the Patreon checkout that preselects the tier.</summary>
    internal static void OpenPlanFromInbox(int tier, string rowKey)
    {
        Log.Information("[Inbox] plan button on {Row} ({Plan})", rowKey, ConversionRules.PlanWord(tier));
        PlanOpener(ConversionRules.PlanUrl(tier));
    }
}
