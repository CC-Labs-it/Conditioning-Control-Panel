using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Read-only entitlement refresh, independent of progression loads and pushes.</summary>
internal static class EntitlementRefresh
{
    private static int _inFlight;
    private static DateTime _lastFocus;
    private static string? _answeredIdentity;
    internal static bool ServerAnsweredThisRun => !string.IsNullOrEmpty(CoreSettings.Current.AuthToken)
        && _answeredIdentity != null && _answeredIdentity == CoreSettings.Current.UnifiedId;
    internal static event Action? Changed;
    internal static int EffectiveTier => CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess
        && !ProviderSubscription.IsInviteWeekOnly(AccountSeed.Patreon, AccountSeed.SubscribeStar, CoreSettings.Current) ? 1 : 0;

    internal static void Install() => Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        window.Activated += (_, _) => OnActivated(window is Views.Windows.MainShellWindow));

    internal static void OnActivated(bool shell)
    {
        if (CoreSettings.Current.OfflineMode) return;
        var now = DateTime.UtcNow;
        var checkout = CheckoutWatch.TakeCheck(now);
        if (!checkout && (!shell || now - _lastFocus < TimeSpan.FromSeconds(20))) return;
        _lastFocus = now;
        _ = CheckAsync(checkout);
    }

    internal static async Task<JustSubscribedOutcome> CheckAsync(bool validatePatreon = true)
    {
        var before = EffectiveTier;
        var s = CoreSettings.Current;
        var signedIn = !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(s.AuthToken);
        var patreon = AccountSeed.Patreon;
        var reached = false;
        if (!s.OfflineMode)
        {
            if (signedIn) reached = await RefreshAsync();
            if (validatePatreon && patreon?.IsAuthenticated == true)
            {
                try { await patreon.ValidateSubscriptionAsync(forceRefresh: true); reached = true; }
                catch (Exception ex) { Log.Debug(ex, "Patreon revalidation failed"); }
            }
        }
        if (EffectiveTier > before) CheckoutWatch.Disarm();
        Changed?.Invoke();
        return JustSubscribedRule.Decide(signedIn || patreon?.IsAuthenticated == true, reached, before, EffectiveTier);
    }

    internal static async Task<bool> RefreshAsync()
    {
        var s = CoreSettings.Current;
        var id = s.UnifiedId;
        if (s.OfflineMode || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(s.AuthToken)
            || Interlocked.Exchange(ref _inFlight, 1) != 0) return false;
        try
        {
            var user = await AccountSeed.NewV2().GetUserProfileAsync(id);
            if (user == null) return false;
            return await Dispatcher.UIThread.InvokeAsync(() => Apply(s, id, user));
        }
        catch (Exception ex) { Log.Debug(ex, "Entitlement refresh failed"); return false; }
        finally { Interlocked.Exchange(ref _inFlight, 0); }
    }

    internal static bool Apply(AppSettings s, string id, V2AuthService.V2User user)
    {
        if (!ReferenceEquals(s, CoreSettings.Current) || s.OfflineMode || s.UnifiedId != id
            || string.IsNullOrEmpty(s.AuthToken)) return false;
        var before = EffectiveTier;
        var tier = EntitlementTierRule.ParseTier(user.EffectiveTierRaw);
        s.ServerTierSeen = EntitlementTierRule.ServerTierSeenAfter(EntitlementTierRule.Decide(s.PatreonTier, tier), tier, s.ServerTierSeen);
        EntitlementTierRule.ApplyRise(s, tier, DateTime.UtcNow);
        PatreonActiveRule.Restamp(s, new PatreonActiveReading(user.PatreonIsActive,
            Services.Invites.InviteRules.ParseUtc(user.PatreonConfirmedAtRaw), user.PatreonTier), DateTime.UtcNow);
        if (tier.HasValue) _answeredIdentity = id;
        CoreSettings.Save();
        if (EffectiveTier > before)
        {
            CheckoutWatch.Disarm();
            AccountSeed.Patreon?.NotifyEntitlementRaised(EffectiveTier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1);
        }
        Changed?.Invoke();
        return true;
    }
}
