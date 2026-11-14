using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
    internal static Func<MainShellWindow?> RecoveryShell = () => MainShellWindow.Current;

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
    /// <param name="intakePass">Tests only: isolates the entitlement subscriptions.</param>
    internal static bool Seed(Func<string, ProviderSubscription>? make = null, Func<ProviderSubscription, DiscordAccount>? makeDiscord = null,
        IntakePassService? intakePass = null)
    {
        PrizeOwnership.Seed();
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
        // New providers, new reading state: no lower reading waiting for its second look, no focus read throttled.
        (Readings, FocusRefresh, _lastLowerLogged) = (new EntitlementReadings(), new EntitlementTierMonitor(), null);
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
        CoreAccount.CurrentTierProvider = () => patreon.CurrentTier;
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
        // WPF App.xaml.cs:416-419: contract D's swap seams, one owner (MergedAccountRecoveryHead).
        MergedAccountRecoveryHead.Seed();
        // WPF ProfileSyncService.HandleUnauthorizedAsync (StartHeartbeat on success) / TryProviderRevalidateAsync.
        AuthRecovery.Recovered = () => { if (Sync is { Loaded: true } sync) sync.StartHeartbeat(); };
        AuthRecovery.ProviderRevalidate = async () =>
        {
            if (patreon.IsAuthenticated) { await patreon.ValidateSubscriptionAsync(forceRefresh: true); return true; }
            if (discord.IsAuthenticated) { await discord.ValidateAndRefreshUserAsync(forceRefresh: true); return true; }
            return false;   // invite-code / email sessions have no provider to mint from
        };
        // WPF App.xaml.cs:3124: the weekly intake pass re-evaluates (and refunds a pre-premium spend)
        // on either provider's TierChanged. Here, because this is where the providers now exist.
        (intakePass ?? App.IntakePass).AttachEntitlementSources(
            h => { patreon.TierChanged += h; substar.TierChanged += h; },
            h => { patreon.TierChanged -= h; substar.TierChanged -= h; });
        return true;
    }

    /// <summary>WPF App.ReauthenticateAfterMergedSwapAsync: mint a fresh token from an existing provider.</summary>
    internal static Task<bool> ReauthenticateAfterMergedSwapAsync() => MergedAccountRecoveryHead.ReauthenticateAsync();

    /// <summary>WPF App.xaml.cs:2267 restore: the unified id persisted from the previous session. Unlike WPF,
    /// only when something can still prove it (an auth token, or a signed-in provider): with no Secret Service
    /// the token is gone after a restart, so the identity is not restored except for a pending merged swap.</summary>
    internal static void RestoreSession()
    {
        var id = CoreSettings.Current.UnifiedId;
        if (string.IsNullOrEmpty(id)) return;
        if (!CoreSettings.Current.MergedSignInPending && string.IsNullOrEmpty(CoreSettings.Current.AuthToken)
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
        // A pending merge: a provider mints the token (WPF App.xaml.cs:4690,4757); when none can, restore-session does (WPF :4805).
        var unproven = CoreSettings.Current.MergedSignInPending && !await MergedAccountRecoveryHead.ReauthenticateAsync();
        v2 ??= NewV2();
        // If a provider already authenticated, it validated the session (and owns the load in WPF).
        if (unproven || (Patreon?.IsAuthenticated != true && Discord?.IsAuthenticated != true))
        {
            Log.Information("Validating restored session for {Id}...", id);
            var outcome = await v2.ValidateRestoredSessionAsync(id);
            if (outcome == V2AuthService.RestoreOutcome.Cleared) { CoreAccount.UnifiedUserId = null; CoreSettings.Current.MergedSignInPending = false; CoreSettings.Save(); }
            if (outcome != V2AuthService.RestoreOutcome.Validated) return;
            if (unproven && !string.IsNullOrEmpty(CoreSettings.Current.AuthToken)) { CoreSettings.Current.MergedSignInPending = false; CoreSettings.Save(); }
        }
        await LoadProfileAsync(v2);
    }

    /// <summary>
    /// WPF ProfileSyncService.LoadProfileAsync's V2 read-before-write (ReadServerProfileBeforePushAsync), READ-ONLY:
    /// GET /v2/user/profile, follow the curve epoch, offer a tier rise, adopt take-higher (Core <see cref="ProfileAdopt"/>).
    /// Then, as WPF LoadProfileAsync does after the read, the heartbeat and the push (<see cref="Sync"/>; unseeded = none).
    /// </summary>
    /// <param name="v2">Tests only.</param>
    internal static async Task<bool> LoadProfileAsync(V2AuthService? v2 = null)
    {
        try
        {
            var uid = CoreSettings.Current.UnifiedId;
            if (!await LoadProfileCoreAsync(v2 ?? NewV2()) || CoreSettings.Current.UnifiedId != uid) return false;
            LoadedProfileUid = uid;
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

    // WPF MainWindow.Login.cs:155-187. The failed-read fallback has its own guards.
    internal static async Task PostLoginAsync(Func<Task<bool>>? load = null, AchievementEngine? achievements = null)
    {
        var uid = CoreSettings.Current.UnifiedId;
        Sync?.StartHeartbeat();
        await Task.Delay(500);
        if (CoreSettings.Current.UnifiedId != uid || string.IsNullOrEmpty(uid)) return;
        achievements ??= App.Achievements;
        if (achievements != null) achievements.SuppressPopups = true;
        try
        {
            load ??= () => LoadProfileAsync();
            var loaded = await load();
            if (!loaded && CoreSettings.Current.UnifiedId == uid)
                loaded = await load();
            // WPF Login.cs:170-181 calls sync even after a failed read.
            if (CoreSettings.Current.UnifiedId == uid && Sync is { } sync)
            {
                if (loaded || sync.Loaded) await sync.PushAsync("after login");
                else
                {
                    var session = sync.SessionVersion;
                    await sync.PushAfterFailedReadAsync();
                    _ = RetryProfileReadAsync(sync, uid, session);
                }
            }
        }
        finally
        {
            if (achievements != null) achievements.SuppressPopups = false;
        }
    }

    private static async Task RetryProfileReadAsync(SyncPush sync, string uid, int session)
    {
        var settings = CoreSettings.Current;
        var delay = 1;
        while (ReferenceEquals(Sync, sync) && sync.SessionVersion == session && !sync.Loaded
            && ReferenceEquals(CoreSettings.Current, settings) && settings.UnifiedId == uid && !settings.OfflineMode)
        {
            await Task.Delay(TimeSpan.FromSeconds(delay));
            if (!ReferenceEquals(Sync, sync) || sync.SessionVersion != session || sync.Loaded
                || !ReferenceEquals(CoreSettings.Current, settings) || settings.UnifiedId != uid || settings.OfflineMode) return;
            if (await LoadProfileAsync())
            {
                if (ReferenceEquals(Sync, sync) && sync.SessionVersion == session && settings.UnifiedId == uid)
                    sync.Nudge("after profile recovery");
                return;
            }
            delay = Math.Min(delay * 2, 60);
        }
    }

    /// <summary>The sync configuration used by desktop startup and account tests.</summary>
    internal static SyncPush NewSync(Func<IEnumerable<string>?> unlocked, Func<bool> running, HttpMessageHandler? http = null)
        => new(unlocked, running, http, Views.Windows.MainShellWindow.SanitizeOwnWardrobe)
        {
            // A minutes lift rebaselines the day log once, through ProfileAdopt.ConditioningMinutesLifted
            // (RebaselineDayLogAfterCloudMerge), for the profile read and the sync reply alike.
            ResetDailyQuest = () => App.Quests?.ForceRegenerateDailyQuest(),
            ResetWeeklyQuest = () => App.Quests?.ForceRegenerateWeeklyQuest(),
            Prizes = PrizeOwnership.Feed,
            Countdown = App.DescentCountdown,
            DescentReader = App.Descent,
            // WPF DescentMigrationService.RaiseStageCeremonyDue (:421-440): the companion carries the moment,
            // scripted copy, no sound, no AI badge.
            StageCeremonyDue = stage => Views.AvatarTube.AvatarTubeWindow.Live?.GigglePriority(
                ConditioningControlPanel.Localization.Loc.GetF("descent_stage_drip",
                    ConditioningControlPanel.Services.Descent.DescentStageCopy.RomanNumeral(stage)),
                playSound: false, aiGenerated: false),
        };

    /// <summary>The push, seeded by the app at startup only: unseeded (every test that does not set it) nothing is pushed.</summary>
    internal static SyncPush? Sync;

    /// <summary>WPF ProfileSyncService.ProfileLoaded: a profile load succeeded (the welcome-back sheet's second look).</summary>
    internal static event Action? ProfileLoaded;
    internal static string? LoadedProfileUid; // WPF ProfileSync.HasLoadedProfile, scoped to this account

    /// <summary>Tests only: the client every default-path load and restore uses.</summary>
    internal static Func<V2AuthService> NewV2 = () => new V2AuthService();

    /// <summary>A confirmed reading ended a grace window: the same repaint as a tier change, with no
    /// celebration, which is why it is not TierChanged (WPF EntitlementTierSync.GraceClosed).</summary>
    internal static event Action? GraceClosed;

    /// <summary>WPF EntitlementTierSync.TierRaised: a live rise, with the new tier (1 or 2), raised BEFORE the
    /// provider's TierChanged so the shell can arm the immediate card and the profile-bubble fanfare.</summary>
    internal static event Action<int>? TierRaised;

    /// <summary>WPF EntitlementTierSync.ReadingApplied: raised after a reading moved the lower tier the server
    /// reported (<c>ServerTierSeen</c> stored or cleared again), for the lapse rows. A close raises
    /// <see cref="GraceClosed"/> instead.</summary>
    internal static event Action? TierReadingApplied;

    /// <summary>Tests only: the clock every reading and the focus throttle read.</summary>
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private static EntitlementReadings Readings = new();
    private static int? _lastLowerLogged;

    /// <summary>WPF EntitlementTierSync.OnAppFocused's throttle (20 s, one read at a time). Core keeps only the
    /// throttle: the reading it fetches lands through <see cref="ApplyTierReading"/> like every other.</summary>
    private static EntitlementTierMonitor FocusRefresh = new();

    /// <summary>
    /// WPF EntitlementTierSync.OnAppFocused + RefreshAsync: the shell came to the front, so the profile is read
    /// again (at most once per <see cref="EntitlementTierMonitor.FocusCheckGap"/>, one read in flight) and its
    /// effective_tier goes through <see cref="ApplyTierReading"/>: a rise unlocks now, a lower reading starts or
    /// confirms the grace close, exactly as the profile load and the heartbeat do; then its Patreon fields go
    /// through <see cref="ApplyPatreonReading"/>. Buy on the site, click back into the app, unlocked about a
    /// second later. Call on the UI thread; never throws.
    /// </summary>
    /// <param name="v2">Tests only.</param>
    internal static async Task OnAppFocusedAsync(V2AuthService? v2 = null)
    {
        try
        {
            await FocusRefresh.OnAppFocusedAsync(CoreSettings.Current, UtcNow(), async id =>
            {
                var node = await (v2 ?? NewV2()).GetUserProfileNodeAsync(id);
                // A late answer for an account that signed out or switched while it ran lands nowhere.
                if (node == null || CoreSettings.Current.UnifiedId != id) return;
                ApplyTierReading(EntitlementTierRule.ParseTier(node["effective_tier"]), "focus");
                ApplyPatreonReading(PatreonActiveRule.Parse(node), "focus");
                ApplyProfileExtras(ConditioningControlPanel.Services.Startup.Conversion.ProfileExtras.Parse(node));
            });
        }
        catch (Exception ex) { Log.Debug("[Entitlement] focus refresh failed: {Error}", ex.Message); }
    }

    /// <summary>Raised on the UI thread after a focus read's conversion fields were applied (the member hello row).</summary>
    internal static event Action? ProfileExtrasApplied;

    /// <summary>WPF EntitlementTierSync.RefreshAsync -> ProfileExtras.Apply: the same focus read carries the free
    /// member flag, paid_until and post grants (no second request). Only what the server sent moves; a change saves
    /// and re-reads the Daily Gift. Never throws.</summary>
    internal static void ApplyProfileExtras(ConditioningControlPanel.Services.Startup.Conversion.ProfileExtrasReading? reading)
    {
        try
        {
            var s = CoreSettings.Current;
            if (reading == null || string.IsNullOrEmpty(s.UnifiedId) || string.IsNullOrEmpty(s.AuthToken)) return;
            if (ConditioningControlPanel.Services.Startup.Conversion.ProfileExtras.Apply(s, reading))
            {
                CoreSettings.Save();
                App.DailyFree?.NotifyInputsChanged();
            }
            ProfileExtrasApplied?.Invoke();
        }
        catch (Exception ex) { Log.Debug("[Entitlement] profile extras failed: {Error}", ex.Message); }
    }

    /// <summary>WPF EntitlementTierSync.OfferPatreon: a fresh server Patreon confirmation (patreon_is_active +
    /// patreon_confirmed_at) re-stamps the 14-day grace from that moment, whatever the tier verdict said, so a
    /// Discord sign-in patron keeps access. It only ever lengthens a window, so it can never end access; the
    /// tier path above owns every lowering. Never throws.</summary>
    internal static void ApplyPatreonReading(PatreonActiveReading? reading, string source)
    {
        try
        {
            var s = CoreSettings.Current;
            if (reading == null || string.IsNullOrEmpty(s.UnifiedId) || string.IsNullOrEmpty(s.AuthToken)) return;
            var before = EffectiveTierNow();
            if (!PatreonActiveRule.Restamp(s, reading, UtcNow())) return;
            Log.Information("[Entitlement] {Source}: Patreon confirmed active at {At:u}, grace now {Until:u}",
                source, reading.ConfirmedAtUtc, s.PatreonPremiumValidUntil);
            CoreSettings.Save();
            // Access came back (a lapsed grace): the same repaint a rise gets.
            var after = EffectiveTierNow();
            if (after <= before) return;
            CheckoutWatch.Disarm();   // WPF EntitlementTierSync.OfferPatreon
            Patreon?.NotifyEntitlementRaised(after >= 2 ? Models.PatreonTier.Level2 : Models.PatreonTier.Level1);
        }
        catch (Exception ex) { Log.Debug("[Entitlement] patreon reading failed ({Source}): {Error}", source, ex.Message); }
    }

    /// <summary>WPF EntitlementTierSync.HoldGraceClose: the window closes after a session or a game,
    /// never during, because a close switches premium features off. Anything unreadable holds.</summary>
    private static bool HoldGraceClose()
    {
        try
        {
            return CoreSession.IsSessionRunning || CoreEngine.IsRunning || LockdownService.Current?.IsActive == true
                || Views.Windows.PanicSurfaces.AnyOwnsTheScreen();
        }
        catch { return true; }
    }

    /// <summary>WPF EntitlementTierSync.EffectiveTierNow: 2 Prime, 1 Basic (an invite week is not a plan), else 0.</summary>
    private static int EffectiveTierNow()
    {
        if (CoreAccount.HasLabAccess) return 2;
        return CoreAccount.HasPremiumAccess && !ProviderSubscription.IsInviteWeekOnly(Patreon, SubscribeStar, CoreSettings.Current) ? 1 : 0;
    }

    // A queued heartbeat can arrive after logout or a new sign-in, even for the same id.
    internal static void ApplyHeartbeatTier(string id, string? token, string body)
    {
        var s = CoreSettings.Current;
        if (s.OfflineMode || s.UnifiedId != id || string.IsNullOrEmpty(token) || s.AuthToken != token) return;
        ApplyTierReading(EntitlementTierRule.ParseHeartbeatTier(body), "heartbeat");
    }

    /// <summary>WPF EntitlementTierSync.ApplyOnUi: THE one server tier reading path of this head, from the
    /// profile load, a heartbeat or the focus refresh. A rise unlocks, a tier renews its grace, and the same
    /// lower reading 90 s later closes it. Never throws.</summary>
    internal static void ApplyTierReading(int? serverTier, string source)
    {
        try
        {
            var s = CoreSettings.Current;
            // WPF ApplyOnUi: any reading with a tier, for a signed-in account, is the server's answer this run.
            if (serverTier.HasValue && !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(s.AuthToken))
                EntitlementRefresh.NoteAnswered(s.UnifiedId);
            var before = EffectiveTierNow();
            var seenBefore = s.ServerTierSeen;
            var reading = Readings.Apply(s, serverTier, HoldGraceClose, UtcNow());
            if (reading.Dirty) CoreSettings.Save();
            if (s.ServerTierSeen != seenBefore && !reading.Closed)
            {
                try { TierReadingApplied?.Invoke(); }
                catch (Exception ex) { Log.Debug("[Entitlement] reading listener failed: {Error}", ex.Message); }
            }
            // WPF: a lower reading keeps the stored tier for its second look (and while a session or a game
            // holds the close); logged once per value.
            if (reading.Verdict == EntitlementVerdict.Lower && !reading.Closed && _lastLowerLogged != serverTier)
            {
                _lastLowerLogged = serverTier;
                Log.Information("[Entitlement] {Source}: server tier {Server} below stored {Local}", source, serverTier, s.PatreonTier);
            }
            if (reading.Rose || reading.Closed) _lastLowerLogged = null;
            if (reading.Closed)
            {
                Log.Information("[Entitlement] {Source}: server tier {Server} closed the grace window", source, serverTier);
                GraceClosed?.Invoke();
                return;
            }
            // A rise, or a renewal that brought back a lapsed window: the doors have to repaint.
            var after = EffectiveTierNow();
            if (!reading.Rose && after <= before) return;
            var tier = reading.Rose ? serverTier!.Value : after;
            Log.Information("[Entitlement] {Source}: access now tier {Tier}", source, tier);
            if (reading.Rose)
            {
                // A new purchase: the rise's own fanfare, before TierChanged lands (WPF EntitlementTierSync).
                try { TierRaised?.Invoke(tier); }
                catch (Exception ex) { Log.Warning(ex, "[Entitlement] TierRaised handler failed"); }
            }
            Patreon?.NotifyEntitlementRaised(tier >= 2 ? Models.PatreonTier.Level2 : Models.PatreonTier.Level1);
        }
        catch (Exception ex) { Log.Warning(ex, "[Entitlement] apply failed ({Source})", source); }
    }

    // A pending migration owns its ledger until the sync ack, also across login/reload.
    internal static void ApplyLoginUserData(ConditioningControlPanel.Models.AppSettings s, V2AuthService.V2User user, string? token, DateTime now)
    {
        if (ConditioningControlPanel.Services.Descent.DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice))
        {
            var highest = s.HighestLevelEver;
            V2AuthService.ApplyIdentity(s, user, token);
            s.HighestLevelEver = Math.Max(highest, s.HighestLevelEver);
            EntitlementTierRule.ExtendGrace(s, user.PatreonTier, now);
        }
        else ProfileAdopt.ApplyUserData(s, user, token, now);
    }

    private static async Task<bool> LoadProfileCoreAsync(V2AuthService v2)
    {
        var s = CoreSettings.Current;
        var id = s.UnifiedId;
        var syncAtRead = Sync;
        var session = syncAtRead?.SessionVersion;
        if (s.OfflineMode || string.IsNullOrEmpty(id)) return false;
        PrizeOwnership.Feed.NoteAccount(id);
        var streakGeneration = LoginStreakHead.ProfileGeneration;
        var user = await v2.GetUserProfileAsync(id);
        if (user == null)
        {
            Log.Warning("Profile load: server profile could not be read for {Id}", id);
            return false;
        }
        // The request may finish after logout or an account/settings switch (or a streak profile clear).
        if (!ReferenceEquals(s, CoreSettings.Current) || s.UnifiedId != id || s.OfflineMode
            || streakGeneration != LoginStreakHead.ProfileGeneration
            || !ReferenceEquals(Sync, syncAtRead) || syncAtRead?.SessionVersion != session) return false;
        // WPF ProfileSyncService: the profile read seeds ownership before the garage opens (#3854).
        PrizeOwnership.Feed.Apply(id, user.UnifiedId, user.Prizes, "V2 profile");
        LoginStreakHead.ProfileLoaded(id, user.Stats);
        ProfileAdopt.ApplyCurveEpoch(s, user.CurveEpoch);
        ApplyTierReading(EntitlementTierRule.ParseTier(user.EffectiveTierRaw), "profile");
        // WPF ProfileSyncService read-before-write: the Patreon confirmation keeps a Discord sign-in's grace alive.
        ApplyPatreonReading(new PatreonActiveReading(user.PatreonIsActive,
            ConditioningControlPanel.Services.Invites.InviteRules.ParseUtc(user.PatreonConfirmedAtRaw), user.PatreonTier), "profile");
        if (!ConditioningControlPanel.Services.Descent.DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice))
            ProfileAdopt.AdoptReadBeforeWrite(s, user, adoptConditioningMinutes: true);
        CoreSettings.Save();
        Log.Information("Profile load: Level {Level} ({Xp} XP into level) after adopt", s.PlayerLevel, (int)s.PlayerXP);
        if (Sync is { } sync)
        {
            sync.MarkLoaded(user.Achievements);
            // The read above lifted conditioning minutes take-higher (WPF ProfileSyncService:3205-3217).
            if (user.TotalConditioningMinutes.HasValue) sync.NoteProgressRead(SyncBody.Field.TotalConditioningMinutes);
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
        CoreSettings.Current.MergedSignInPending = false;
        var s = CoreSettings.Current;
        s.UnifiedId = null;
        s.AuthToken = null;
        s.UserDisplayName = null;
        s.HasLinkedDiscord = false;
        s.HasLinkedPatreon = false;
        ProgressionClear.Apply(s, clearMigration: true);
        if (accountDeleted) App.Quests?.ResetProgress();
        CoreSettings.Save();
        LoginStreakHead.ClearProfile();
        App.Achievements?.Reset();
        // SyncPush.Reset clears its Prizes feed (seeded with PrizeOwnership.Feed); unseeded, clear it here.
        if (Sync is { } push) push.Reset();
        else PrizeOwnership.Feed.Clear();
    }

    /// <summary>All three validate at once, as WPF starts them (App.xaml.cs). Never throws.</summary>
    internal static Task InitializeAsync() => Task.WhenAll(
        Patreon?.InitializeAsync() ?? Task.CompletedTask,
        SubscribeStar?.InitializeAsync() ?? Task.CompletedTask,
        Discord?.InitializeAsync() ?? Task.CompletedTask);
}
