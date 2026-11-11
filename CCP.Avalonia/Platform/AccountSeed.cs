using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Seeds <see cref="CoreAccount"/> from Core's <see cref="ProviderSubscription"/>s and <see cref="DiscordAccount"/>,
/// tokens via CoreSecrets. FAIL CLOSED: a provider that cannot be built seeds nothing; CoreAccount turns a throw into "no".
/// Cloud load (unit 6), then the known-fields-only push and heartbeat (unit 7c, Core <see cref="SyncPush"/>); no Rich Presence.</summary>
internal static class AccountSeed
{
    internal static ProviderSubscription? Patreon { get; private set; }
    internal static ProviderSubscription? SubscribeStar { get; private set; }
    internal static DiscordAccount? Discord { get; private set; }

    /// <summary>True when the last <see cref="Seed"/> granted the local sandbox whitelist (see
    /// <see cref="SandboxWhitelistRequested"/>). Always false in a Release build.</summary>
    internal static bool SandboxWhitelisted { get; private set; }

    /// <summary>Decision 2026-10-12 (docs/avalonia-decisions.html): a sandboxed DEBUG run (CCP_USERDATA_DIR honoured,
    /// so SandboxNet is active and there is no network or real account) with CCP_SANDBOX_WHITELIST=1 is treated as a
    /// whitelisted account: premium + Lab, as the server whitelist would grant. Only `kc start` sets the variable;
    /// headless tests do not, so they keep testing the gates. Release builds compile this out: shipped builds keep the paywall.</summary>
    internal static bool SandboxWhitelistRequested()
    {
#if DEBUG
        return SandboxNet.Active && Environment.GetEnvironmentVariable("CCP_SANDBOX_WHITELIST") == "1";
#else
        return false;
#endif
    }

    /// <param name="make">Tests only: builds the provider for a prefix.</param>
    /// <param name="makeDiscord">Tests only: builds the Discord account.</param>
    internal static bool Seed(Func<string, ProviderSubscription>? make = null, Func<ProviderSubscription, DiscordAccount>? makeDiscord = null)
    {
        make ??= prefix => new ProviderSubscription(prefix);
        makeDiscord ??= peer => new DiscordAccount(() => peer);
        ProviderSubscription patreon, substar;
        DiscordAccount discord;
        try { patreon = make("patreon"); substar = make("substar"); discord = makeDiscord(patreon); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Account providers unavailable: signed out, no entitlement");
            return false;
        }
        (Patreon, SubscribeStar, Discord) = (patreon, substar, discord);
        patreon.PeerDisplayName = () => discord.CustomDisplayName; // WPF PatreonService

        // WPF App.IsLoggedIn / UserDisplayName / the PatreonService gates.
        CoreAccount.IsLoggedInProvider = () => patreon.IsAuthenticated || discord.IsAuthenticated
            || substar.IsAuthenticated || !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);
        CoreAccount.DisplayNameProvider = () =>
        {
            var s = CoreSettings.Current;
            if (s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername)) return s.OfflineUsername;
            return s.UserDisplayName ?? patreon.DisplayName ?? discord.CustomDisplayName ?? discord.DisplayName ?? substar.DisplayName;
        };
        SandboxWhitelisted = SandboxWhitelistRequested();
        var sandbox = SandboxWhitelisted;
        CoreAccount.IsWhitelistedProvider = () => sandbox || patreon.IsWhitelisted;
        CoreAccount.HasPremiumAccessProvider = () => sandbox || ProviderSubscription.HasPremiumAccess(patreon, substar, CoreSettings.Current);
        CoreAccount.HasLabAccessProvider = () => sandbox || ProviderSubscription.HasLabAccess(patreon, substar, CoreSettings.Current);
        // WPF App.xaml.cs:487-488: TierGate's seam reads the same gates (Patreon.HasPremiumAccess there).
        CoreEntitlement.HasPremiumProvider = () => CoreAccount.HasPremiumAccess;
        CoreEntitlement.HasLabProvider = () => CoreAccount.HasLabAccess;
        CoreProgram.HasPremiumProvider = () => CoreAccount.HasPremiumAccess; // WPF App.xaml.cs:426
        // WPF App.xaml.cs:561-562 (ProfileSync rename/delete), with its "no unified id" precondition.
        CoreAccount.ChangeDisplayNameProvider = name => CoreSettings.Current.UnifiedId is { Length: > 0 } id
            ? NewV2().ChangeDisplayNameAsync(id, name) : null;
        CoreAccount.DeleteAccountProvider = () => CoreSettings.Current.UnifiedId is { Length: > 0 } id
            ? NewV2().DeleteAccountAsync(id) : null;
        // WPF ProfileSyncService.HandleUnauthorizedAsync (StartHeartbeat on success) / TryProviderRevalidateAsync.
        AuthRecovery.Recovered = () => { if (Sync is { Loaded: true } sync) sync.StartHeartbeat(); };
        AuthRecovery.ProviderRevalidate = async () =>
        {
            if (patreon.IsAuthenticated) { await patreon.ValidateSubscriptionAsync(forceRefresh: true); return true; }
            if (discord.IsAuthenticated) { await discord.ValidateAndRefreshUserAsync(forceRefresh: true); return true; }
            return false;   // invite-code / email sessions have no provider to mint from
        };
        // WPF App.xaml.cs:2937: the weekly intake pass re-evaluates (and refunds a pre-premium spend)
        // on either provider's TierChanged. Here, because this is where the providers now exist.
        App.IntakePass.AttachEntitlementSources(
            h => { patreon.TierChanged += h; substar.TierChanged += h; },
            h => { patreon.TierChanged -= h; substar.TierChanged -= h; });
        return true;
    }

    /// <summary>WPF App.xaml.cs:2267 restore: the unified id persisted from the previous session. Unlike WPF,
    /// only when something can still prove it (an auth token, or a signed-in provider): with no Secret Service
    /// the token is gone after a restart, and showing "signed in" with nothing behind it is worse than signed out.</summary>
    internal static void RestoreSession()
    {
        var id = CoreSettings.Current.UnifiedId;
        if (string.IsNullOrEmpty(id)) return;
        if (string.IsNullOrEmpty(CoreSettings.Current.AuthToken)
            && Patreon?.IsAuthenticated != true && Discord?.IsAuthenticated != true && SubscribeStar?.IsAuthenticated != true)
        {
            Log.Information("Stored UnifiedUserId not restored: no auth token and no signed-in provider");
            return;
        }
        CoreAccount.UnifiedUserId = id;
        Log.Information("Restored UnifiedUserId from settings: {Id}", id);
    }

    /// <summary>WPF App.ValidateRestoredSessionAsync (Core <see cref="V2AuthService.ValidateRestoredSessionAsync"/>), run after
    /// the providers validated, then the cloud profile load that follows it. WPF's provider init paths load the profile when
    /// a provider authenticated, and this path loads it when the session validated; either way it loads here.</summary>
    /// <param name="v2">Tests only.</param>
    internal static async Task ValidateRestoredSessionAsync(V2AuthService? v2 = null)
    {
        var id = CoreAccount.UnifiedUserId;
        if (string.IsNullOrEmpty(id) || CoreSettings.Current.OfflineMode) return;
        v2 ??= NewV2();
        // If a provider already authenticated, it validated the session (and owns the load in WPF).
        if (Patreon?.IsAuthenticated != true && Discord?.IsAuthenticated != true)
        {
            Log.Information("Validating restored session for {Id}...", id);
            var outcome = await v2.ValidateRestoredSessionAsync(id);
            if (outcome == V2AuthService.RestoreOutcome.Cleared) CoreAccount.UnifiedUserId = null;
            if (outcome != V2AuthService.RestoreOutcome.Validated) return;
        }
        await LoadProfileAsync(v2);
    }

    /// <summary>
    /// WPF ProfileSyncService.LoadProfileAsync's V2 read-before-write (ReadServerProfileBeforePushAsync), READ-ONLY:
    /// GET /v2/user/profile, follow the curve epoch, offer a tier rise, adopt take-higher (Core <see cref="ProfileAdopt"/>).
    /// Then, as WPF LoadProfileAsync does after the read, the heartbeat and the push (<see cref="Sync"/>; unseeded = none).
    /// ponytail: no season-recap nudge - the next layer.
    /// </summary>
    /// <param name="v2">Tests only.</param>
    internal static async Task<bool> LoadProfileAsync(V2AuthService? v2 = null)
    {
        try
        {
            if (!await LoadProfileCoreAsync(v2 ?? NewV2())) return false;
            ProfileLoaded?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            // Fire-and-forget from the login dialog and inside an async void Post at startup: never throw.
            Log.Warning(ex, "Profile load failed");
            return false;
        }
    }

    // WPF EntitlementTierSync.cs:28-33: remembered lower tiers alone are never a current reading.
    // Tie the answer to the account so a response for another sign-in cannot authorize a notice.
    internal static string? TierAnswerAccount;
    internal static bool ServerAnsweredThisRun => !string.IsNullOrEmpty(CoreSettings.Current.AuthToken)
        && !string.IsNullOrEmpty(CoreSettings.Current.UnifiedId)
        && TierAnswerAccount == CoreSettings.Current.UnifiedId;

    internal static event Action? TierReadingApplied;
    internal static DateTime LastFocusUtc = DateTime.MinValue;
    internal static DateTime CheckoutUntilUtc = DateTime.MinValue;
    internal static DateTime LastCheckoutUtc = DateTime.MinValue;
    private static int _refreshInFlight;

    // WPF EntitlementTierSync.cs:180, 224: focus reads the tier without pushing a profile.
    internal static void OnFocus()
    {
        var now = DateTime.UtcNow;
        var s = CoreSettings.Current;
        if (s.OfflineMode || string.IsNullOrEmpty(s.UnifiedId) || string.IsNullOrEmpty(s.AuthToken)) return;
        var checkout = now <= CheckoutUntilUtc && (now < LastCheckoutUtc || now - LastCheckoutUtc >= TimeSpan.FromSeconds(20));
        if (!checkout && now - LastFocusUtc < TimeSpan.FromSeconds(20)) return;
        if (checkout) LastCheckoutUtc = now;
        LastFocusUtc = now;
        _ = RefreshTierAsync();
        if (checkout && Patreon?.IsAuthenticated == true)
            _ = Patreon.ValidateSubscriptionAsync(forceRefresh: true);
    }

    // WPF Application.Activated also checks a checkout when another app window comes forward.
    internal static void OnCheckoutFocus()
    {
        var now = DateTime.UtcNow;
        if (now <= CheckoutUntilUtc && (now < LastCheckoutUtc || now - LastCheckoutUtc >= TimeSpan.FromSeconds(20)))
            OnFocus();
    }

    internal static void NoteCheckout() => CheckoutUntilUtc = DateTime.UtcNow.AddMinutes(30);

    private static async Task RefreshTierAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _refreshInFlight, 1) != 0) return;
        try
        {
            var s = CoreSettings.Current;
            var id = s.UnifiedId;
            var token = s.AuthToken;
            if (s.OfflineMode || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token)) return;
            var node = await NewV2().GetUserProfileNodeAsync(id);
            Dispatcher.UIThread.Post(() => ApplyProfileReading(id, token, node));
        }
        catch (Exception ex) { Log.Debug("[Entitlement] focus refresh: {Error}", ex.Message); }
        finally { System.Threading.Interlocked.Exchange(ref _refreshInFlight, 0); }
    }

    internal static void ApplyProfileReading(string id, string? token, Newtonsoft.Json.Linq.JObject? node)
    {
        var s = CoreSettings.Current;
        if (node == null || s.UnifiedId != id || string.IsNullOrEmpty(token) || token != s.AuthToken) return;
        var before = Patreon == null ? 0 : CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess && !ProviderSubscription.IsInviteWeekOnly(Patreon, SubscribeStar, s) ? 1 : 0;
        if (PatreonActiveRule.Restamp(s, PatreonActiveRule.Parse(node), DateTime.UtcNow)) {
            CoreSettings.Save();
            var after = Patreon == null ? 0 : CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess && !ProviderSubscription.IsInviteWeekOnly(Patreon, SubscribeStar, s) ? 1 : 0;
            if (after > before) { CheckoutUntilUtc = DateTime.MinValue; try { Patreon?.NotifyEntitlementRaised(after >= 2 ? Models.PatreonTier.Level2 : Models.PatreonTier.Level1); } catch (Exception ex) { Log.Debug("[Entitlement] confirmation listener: {Error}", ex.Message); } }
        }
        ApplyTier(id, token, EntitlementTierRule.ParseTier(node["effective_tier"]));
    }
    internal static void OfferTier(string id, string? token, int? tier)
    {
        if (tier != null) Dispatcher.UIThread.Post(() => ApplyTier(id, token, tier));
    }

    internal static void ApplyTier(string id, string? token, int? tier)
    {
        var s = CoreSettings.Current;
        if (s.UnifiedId != id || string.IsNullOrEmpty(token) || token != s.AuthToken || tier is < 0 or > 2 || tier == null) return;
        TierAnswerAccount = id;
        var verdict = EntitlementTierRule.Decide(s.PatreonTier, tier);
        s.ServerTierSeen = EntitlementTierRule.ServerTierSeenAfter(verdict, tier, s.ServerTierSeen);
        var rose = EntitlementTierRule.ApplyRise(s, tier, DateTime.UtcNow);
        if (rose) CheckoutUntilUtc = DateTime.MinValue;
        CoreSettings.Save();
        try { TierReadingApplied?.Invoke(); }
        catch (Exception ex) { Log.Debug("[Entitlement] reading listener: {Error}", ex.Message); }
        if (rose)
        {
            try { Patreon?.NotifyEntitlementRaised(tier >= 2 ? Models.PatreonTier.Level2 : Models.PatreonTier.Level1); }
            catch (Exception ex) { Log.Debug("[Entitlement] rise listener: {Error}", ex.Message); }
        }
    }

    /// <summary>The push, seeded by the app at startup only: unseeded nothing is pushed.</summary>
    internal static SyncPush? Sync;

    /// <summary>WPF ProfileSyncService.ProfileLoaded: a profile load succeeded (the welcome-back sheet's second look).</summary>
    internal static event Action? ProfileLoaded;

    /// <summary>Tests only: the client every default-path load and restore uses.</summary>
    internal static Func<V2AuthService> NewV2 = () => new V2AuthService();

    private static async Task<bool> LoadProfileCoreAsync(V2AuthService v2)
    {
        var s = CoreSettings.Current;
        var id = s.UnifiedId;
        var token = s.AuthToken;
        if (s.OfflineMode || string.IsNullOrEmpty(id)) return false;
        var node = await v2.GetUserProfileNodeAsync(id);
        var user = node?.ToObject<V2AuthService.V2User>();
        if (user == null)
        {
            Log.Warning("Profile load: server profile could not be read for {Id}", id);
            return false;
        }
        ProfileAdopt.ApplyCurveEpoch(s, user.CurveEpoch);
        // WPF EntitlementTierSync.cs:60-90: only a valid authenticated answer counts.
        ApplyProfileReading(id, token, node);
        ProfileAdopt.AdoptReadBeforeWrite(s, user);
        CoreSettings.Save();
        Log.Information("Profile load: Level {Level} ({Xp} XP into level) after adopt", s.PlayerLevel, (int)s.PlayerXP);
        if (Sync is { } sync)
        {
            sync.MarkLoaded(user.Achievements);
            sync.StartHeartbeat();
            await sync.PushAsync("after load");
        }
        return true;
    }

    /// <summary>Signs a provider out (WPF AccountService.LogoutProvider).</summary>
    internal static void LogoutProvider(string? provider)
    {
        switch (provider)
        {
            case "patreon": Patreon?.Logout(); break;
            case "discord": Discord?.Logout(); break;
            case "substar": SubscribeStar?.Logout(); break;
        }
    }

    /// <summary>
    /// WPF BtnQuickLogout_Click + the identity half of ClearAccountData (MainWindow.Login.cs:320-410):
    /// every provider out, identity and the rotated auth token (#455) cleared. Tokens and identity ONLY.
    /// Order (unit 7c): pre-logout push if loaded -> stop heartbeat -> providers and identity -> progression clear
    /// (Core <see cref="ProgressionClear"/> + achievements, WPF ClearProgressionData) -> the loaded flag reset.
    /// </summary>
    /// <param name="accountDeleted">WPF BtnDeleteProfile_Click: the account is gone server-side, so nothing is pushed
    /// and the quest file goes too (ClearAccountData(wipeQuestProgress: true)).</param>
    internal static async Task Logout(bool accountDeleted = false)
    {
        if (!accountDeleted && Sync is { Loaded: true } sync) await sync.PushAsync("pre-logout", waitForGate: true);
        Sync?.StopHeartbeat();
        SecretStore.ClearFailed = false;
        Patreon?.Logout();
        Discord?.Logout();
        SubscribeStar?.Logout();
        CoreAccount.UnifiedUserId = null;
        TierAnswerAccount = null;
        var s = CoreSettings.Current;
        s.UnifiedId = null;
        s.AuthToken = null;
        s.UserDisplayName = null;
        s.HasLinkedDiscord = false;
        s.HasLinkedPatreon = false;
        ProgressionClear.Apply(s);
        if (accountDeleted) App.Quests?.ResetProgress();
        CoreSettings.Save();
        App.Achievements?.Reset();
        Sync?.Reset();
    }

    /// <summary>All three validate at once, as WPF starts them (App.xaml.cs). Never throws.</summary>
    internal static Task InitializeAsync() => Task.WhenAll(
        Patreon?.InitializeAsync() ?? Task.CompletedTask,
        SubscribeStar?.InitializeAsync() ?? Task.CompletedTask,
        Discord?.InitializeAsync() ?? Task.CompletedTask);
}
