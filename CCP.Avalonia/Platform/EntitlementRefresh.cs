using System;
using System.Runtime.CompilerServices;
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
    private static string? _answeredIdentity;
    internal static bool ServerAnsweredThisRun => !string.IsNullOrEmpty(CoreSettings.Current.AuthToken)
        && _answeredIdentity != null && _answeredIdentity == CoreSettings.Current.UnifiedId;
    internal static event Action? Changed;
    internal static int EffectiveTier => CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess
        && !ProviderSubscription.IsInviteWeekOnly(AccountSeed.Patreon, AccountSeed.SubscribeStar, CoreSettings.Current) ? 1 : 0;

    private static readonly ConditionalWeakTable<Window, object> Hooked = new();
    internal static Action<bool> Activation = shell => _ = OnActivated(shell); // Window callback seam, before throttling.
    internal static IDisposable Install() => Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
    {
        if (Hooked.TryAdd(window, window)) window.Activated += (_, _) => Activation(window is Views.Windows.MainShellWindow);
    });

    /// <summary>Any window coming back after a checkout link re-reads the tier and the provider. A plain return
    /// to the panel is AccountSeed.OnAppFocusedAsync's (WPF EntitlementTierSync.OnAppFocused, one read per 20 s),
    /// so this hook never adds a second profile read for it.</summary>
    internal static Task OnActivated(bool shell)
    {
        if (CoreSettings.Current.OfflineMode) return Task.CompletedTask;
        if (!CheckoutWatch.TakeCheck(DateTime.UtcNow)) return Task.CompletedTask;
        return CheckAsync(true, afterCheckout: true);
    }

    internal static async Task<JustSubscribedOutcome> CheckAsync(bool validatePatreon = true, bool afterCheckout = false)
    {
        var before = EffectiveTier;
        var s = CoreSettings.Current;
        var signedIn = !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(s.AuthToken);
        var patreon = AccountSeed.Patreon;
        var reached = false;
        var profile = signedIn && !s.OfflineMode ? RefreshAsync() : Task.FromResult(false);
        var validation = afterCheckout && validatePatreon && !s.OfflineMode && patreon?.IsAuthenticated == true ? Task.Run(() => patreon.ValidateSubscriptionAsync(forceRefresh: true)) : null;
        if (!s.OfflineMode)
        {
            if (signedIn) reached = await profile;
            if (validatePatreon && (validation != null || patreon?.IsAuthenticated == true))
            {
                try { await (validation ?? patreon!.ValidateSubscriptionAsync(forceRefresh: true)); reached = true; }
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

    internal static bool Apply(AppSettings s, string id, V2AuthService.V2User user, string source = "refresh")
    {
        if (!ReferenceEquals(s, CoreSettings.Current) || s.OfflineMode || s.UnifiedId != id
            || string.IsNullOrEmpty(s.AuthToken)) return false;
        var before = EffectiveTier;
        // WPF EntitlementTierSync.Offer -> ApplyOnUi: the one grace-aware tier path (a rise unlocks, a tier
        // renews the grace, the same lower reading 90 s later closes it), never a rise-only write.
        AccountSeed.ApplyTierReading(EntitlementTierRule.ParseTier(user.EffectiveTierRaw), source);
        // WPF EntitlementTierSync.OfferPatreon on the same read.
        AccountSeed.ApplyPatreonReading(new PatreonActiveReading(user.PatreonIsActive,
            Services.Invites.InviteRules.ParseUtc(user.PatreonConfirmedAtRaw), user.PatreonTier), source);
        if (EffectiveTier > before) CheckoutWatch.Disarm();
        Changed?.Invoke();
        return true;
    }

    /// <summary>WPF EntitlementTierSync.ServerAnsweredThisRun: set by every tier reading for this identity
    /// (<see cref="AccountSeed.ApplyTierReading"/>: profile load, heartbeat, focus and checkout reads).</summary>
    internal static void NoteAnswered(string id) => _answeredIdentity = id;
}
