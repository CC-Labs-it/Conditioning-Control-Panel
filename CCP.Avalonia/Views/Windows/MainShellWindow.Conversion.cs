using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Startup.Conversion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal static Func<V2AuthService>? ConversionClient;
    private DateTime _conversionCheckUtc;
    private bool _conversionChecking;
    private bool _conversionClosed;
    internal const string MemberHelloKey = "free_member_hello";

    private void InitializeConversionProfile()
    {
        Opened += (_, _) => { MaybePostMemberHello(); _ = RefreshConversionProfileAsync(); };
        Activated += (_, _) => _ = RefreshConversionProfileAsync();
        Closed += (_, _) => _conversionClosed = true;
    }

    // WPF EntitlementTierSync.OnAppFocused: throttle before the await, then apply on the UI thread.
    internal async Task RefreshConversionProfileAsync()
    {
        var s = CoreSettings.Current;
        var id = s.UnifiedId;
        if (_conversionClosed || _conversionChecking || ConversionClient == null || s.OfflineMode
            || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(s.AuthToken)
            || DateTime.UtcNow - _conversionCheckUtc < TimeSpan.FromSeconds(20)) return;
        _conversionChecking = true;
        _conversionCheckUtc = DateTime.UtcNow;
        try
        {
            var node = await ConversionClient().GetUserProfileNodeAsync(id);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyConversionExtras(id, ProfileExtras.Parse(node)));
        }
        catch (Exception ex) { Log.Debug("Conversion profile refresh skipped: {Kind}", ex.GetType().Name); }
        finally { _conversionChecking = false; }
    }

    internal void ApplyConversionExtras(string id, ProfileExtrasReading? reading)
    {
        var s = CoreSettings.Current;
        if (_conversionClosed || s.UnifiedId != id || string.IsNullOrEmpty(s.AuthToken)) return;
        if (ProfileExtras.Apply(s, reading))
        {
            CoreSettings.Save();
            App.DailyFree?.NotifyInputsChanged();
        }
        MaybePostMemberHello();
    }

    internal static bool IsFreeMember => CoreSettings.Current.PatreonMember && !CoreAccount.HasPremiumAccess;

    internal void MaybePostMemberHello()
    {
        var s = CoreSettings.Current;
        if (!IsFreeMember || (s.InboxStamps.TryGetValue(MemberHelloKey, out var stamp) && stamp == "1")) return;
        s.InboxStamps[MemberHelloKey] = "1";
        CoreSettings.Save();
        var title = Loc.Get("conv_member_hello_title");
        var summary = Loc.Get("conv_member_hello_body");
        Platform.StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "conv:" + MemberHelloKey, Glyph = "🎁", Title = title, Summary = summary,
            Open = () => _ = Dialogs.MessageDialog.ShowAsync(this, title, MemberHelloBody(summary)),
        });
    }

    // Resolve at open time: a row can wait in the Inbox across a day change.
    internal static string MemberHelloBody(string summary)
    {
        var second = App.DailyFree?.MemberKey;
        var feature = second == null ? null : ExclusiveFeature.All.FirstOrDefault(f => f.DailyFreeKey == second);
        return feature == null ? summary : summary + " " + Loc.GetF("conv_member_second", Loc.Get(feature.TitleLocKey));
    }
}
