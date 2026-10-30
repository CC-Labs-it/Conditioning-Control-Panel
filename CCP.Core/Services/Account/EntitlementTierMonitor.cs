using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
namespace ConditioningControlPanel.Services;
/// <summary>WPF Services/Account/EntitlementTierSync.cs:59,182: live tier rule and focus throttle.
/// The head calls Apply on its UI thread and raises the provider event after TierRaised.</summary>
public sealed class EntitlementTierMonitor
{
    public static readonly TimeSpan FocusCheckGap = TimeSpan.FromSeconds(20);
    private DateTime _lastFocusCheckUtc = DateTime.MinValue;
    private int _inFlight;
    public event Action<int>? TierRaised;
    public bool Apply(AppSettings settings, string accountId, int? tier, DateTime nowUtc, Action save)
    {
        if (settings.OfflineMode || string.IsNullOrEmpty(settings.AuthToken)
            || string.IsNullOrEmpty(accountId) || settings.UnifiedId != accountId
            || EntitlementTierRule.Decide(settings.PatreonTier, tier) == EntitlementVerdict.NoSignal) return false;
        var verdict = EntitlementTierRule.Decide(settings.PatreonTier, tier);
        var seen = EntitlementTierRule.ServerTierSeenAfter(verdict, tier, settings.ServerTierSeen);
        var changed = seen != settings.ServerTierSeen;
        settings.ServerTierSeen = seen;
        var rose = EntitlementTierRule.ApplyRise(settings, tier, nowUtc);
        if (changed || rose) save();
        if (rose)
        {
            try { TierRaised?.Invoke(tier!.Value); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Tier rise observer failed"); }
        }
        return rose;
    }
    /// <summary>One read at a time, at most once per 20 seconds; offline/unsigned never reads.
    /// A late reading carries the original account id so Apply rejects it after account switching.</summary>
    public async Task OnAppFocusedAsync(AppSettings settings, DateTime nowUtc, Func<string, Task> read)
    {
        if (settings.OfflineMode || string.IsNullOrEmpty(settings.UnifiedId) || string.IsNullOrEmpty(settings.AuthToken)
            || nowUtc - _lastFocusCheckUtc < FocusCheckGap || Interlocked.Exchange(ref _inFlight, 1) == 1) return;
        _lastFocusCheckUtc = nowUtc;
        try { await read(settings.UnifiedId); }
        finally { Interlocked.Exchange(ref _inFlight, 0); }
    }
}
