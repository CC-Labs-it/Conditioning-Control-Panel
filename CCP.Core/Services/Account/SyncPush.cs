using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// <c>POST /v2/user/sync</c> for a head that loaded only <c>GET /v2/user/profile</c> (WPF ProfileSyncService.SyncProfileAsync,
    /// cut to what is known): baseline fields, owned consents, adopted skills/progress, cosmetics and claim ack only.
    /// Achievements = server-loaded ∪ local. Gates, all
    /// required: signed in, loaded THIS session (<see cref="MarkLoaded"/>), the 30 s cooldown and the XP watermark.
    /// The post-login failed-read fallback alone may send guarded XP and level without opening the loaded gate.
    /// Also the 120 s heartbeat and the coalesced XP nudge. No periodic push (WPF has none).
    /// ponytail: no 401 recovery or heartbeat adopt - add with the features that need them.
    /// </summary>
    public sealed class SyncPush
    {
        public const SyncBody.Field Sent = SyncBody.Field.UnifiedId | SyncBody.Field.Xp | SyncBody.Field.Level
            | SyncBody.Field.DescentEpoch | SyncBody.Field.DescentAuto | SyncBody.Field.Achievements;
        public const SyncBody.Field Privacy = SyncBody.Field.AllowDiscordDm | SyncBody.Field.ShowOnlineStatus
            | SyncBody.Field.ShareProfilePicture | SyncBody.Field.PublicShareAvatar
            | SyncBody.Field.GoonShareAvatar | SyncBody.Field.GoonShareDm;

        /// <summary>Only audiences explicitly chosen here for this account are known; unrelated defaults never leave.</summary>
        public static SyncBody.Field ConsentFields(AppSettings s) =>
            !string.IsNullOrEmpty(s.UnifiedId) && string.Equals(s.ConsentOwnedAccount, s.UnifiedId, StringComparison.Ordinal)
                ? (SyncBody.Field)s.ConsentOwnedFields & Privacy : SyncBody.Field.None;

        /// <summary>WPF's push-on-change privacy handlers. The owned fields are saved even offline and every later
        /// push (the after-load push included) carries them, so no separate retry flag is needed. Adapted from parity/trunk 4f739180ca68a1e45f62c70a78d2bfbd0f1afcc0, owning each field
        /// individually so changing one switch does not overwrite other audiences with fresh-install defaults.</summary>
        public async Task<bool> PushPrivacyAsync(SyncBody.Field field)
        {
            var s = CoreSettings.Current;
            if (!RememberPrivacyChange(s, field)) return false;
            CoreSettings.Save();
            if (!Loaded || !SignedIn(s)) return false;
            if (await PushAsync("privacy", waitForGate: true)) return true;
            Nudge("privacy");
            return false;
        }

        public static bool RememberPrivacyChange(AppSettings s, SyncBody.Field field)
        {
            if (field == SyncBody.Field.None || (field & Privacy) != field) throw new ArgumentOutOfRangeException(nameof(field));
            if (string.IsNullOrEmpty(s.UnifiedId)) return false;
            if (!string.Equals(s.ConsentOwnedAccount, s.UnifiedId, StringComparison.Ordinal)) s.ConsentOwnedFields = 0;
            s.ConsentOwnedAccount = s.UnifiedId;
            s.ConsentOwnedFields |= (long)field;
            return true;
        }

        // settings.json keys of the six privacy switches (AppSettings' serialized names).
        private static readonly (string Key, SyncBody.Field Field)[] PrivacyKeys =
        {
            ("AllowDiscordDm", SyncBody.Field.AllowDiscordDm), ("ShowOnlineStatus", SyncBody.Field.ShowOnlineStatus),
            ("ShareProfilePicture", SyncBody.Field.ShareProfilePicture), ("PublicShareRealAvatar", SyncBody.Field.PublicShareAvatar),
            ("goonShareAvatar", SyncBody.Field.GoonShareAvatar), ("goonShareDiscordDm", SyncBody.Field.GoonShareDm),
        };

        /// <summary>Decision 2026-10-22 (option C, oracle-deep): a settings.json written by WPF (no <c>consentOwnedFields</c>
        /// key, so no consent-aware build ever saved it) with a non-empty UnifiedId and all six privacy switches as JSON bools
        /// hands that account ownership of all six: WPF sent all six on every sync, so the local values are the server's.
        /// Anything else (fresh install, no UnifiedId, any of the six missing or not a bool) seeds nothing and stays scoped (B).</summary>
        public static void SeedFromWpfSettings(AppSettings s, JObject raw)
        {
            if (raw.ContainsKey("consentOwnedFields") || raw.ContainsKey("consentOwnedAccount")) return;
            if (string.IsNullOrEmpty(s.UnifiedId)
                || raw["UnifiedId"] is not JValue { Type: JTokenType.String } id || (string?)id != s.UnifiedId) return;
            var read = SyncBody.Field.None;
            foreach (var (key, field) in PrivacyKeys)
                if (raw[key]?.Type == JTokenType.Boolean) read |= field;
            if (read != Privacy) return;
            s.ConsentOwnedAccount = s.UnifiedId;
            s.ConsentOwnedFields = (long)read;
        }

        public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan NudgeSettle = TimeSpan.FromSeconds(3), NudgeCooldownSlack = TimeSpan.FromSeconds(2);
        private const string ServerUrl = "https://codebambi-proxy.vercel.app";

        private readonly HttpClient _http;
        private readonly HttpMessageHandler? _handler;
        private readonly Func<IEnumerable<string>?> _localAchievements;
        private readonly Func<bool> _inSession;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string[]? _serverAchievements;   // null: the profile carried none, so achievements are not known
        private Timer? _heartbeat;
        private int _nudgePending;
        private int _sessionVersion;
        private readonly Func<ProfileCosmetics?, ProfileCosmetics>? _sanitizeCosmetics;
        private volatile bool _pendingCosmeticsClear;
        private SyncBody.Field _knownProgress;
        private volatile bool _adopted;
        public bool Adopted => _adopted;
        public const SyncBody.Field AfterAdopt = SyncBody.Field.UnlockedSkills | SyncBody.Field.SkillPoints;
        public Action? LifetimeLifted { get; set; }
        public Action? ResetDailyQuest { get; set; }
        public Action? ResetWeeklyQuest { get; set; }

        /// <summary>WPF ProfileSyncService.PendingCosmeticsClear: set by an EMPTY Customize save (unequip everything),
        /// cleared once a sync carrying the clear succeeds. In memory, as WPF.</summary>
        public bool PendingCosmeticsClear => _pendingCosmeticsClear;

        /// <summary>Tests only.</summary>
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;

        // Failure backoff (release/6.11.5, WPF ProfileSyncService.NoteSyncFailureForBackoff). Touched only inside _gate.
        public event EventHandler<int>? SyncHealthChanged;
        public int ConsecutiveSyncFailures { get; private set; }

        private bool _hasSyncError;
        private int _backoffFailures;
        private DateTime? _blockedUntilUtc;
        private string? _backoffToken;
        private TimeSpan _backoffOffset;

        /// <summary>WPF ProfileSyncService.IsExpectedCancellation: cancelled or disposed, directly or one level down.</summary>
        internal static bool IsExpectedCancellation(Exception ex) =>
            ex is OperationCanceledException or ObjectDisposedException
            || ex.InnerException is OperationCanceledException or ObjectDisposedException;

        private void NoteFailureForBackoff(int? status, string? tokenUsed)
        {
            _hasSyncError = true;
            ConsecutiveSyncFailures++;
            SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
            _backoffFailures++;
            var wait = SyncFailureBackoff.Delay(_backoffFailures);
            _blockedUntilUtc = UtcNow() + wait;
            (_backoffToken, _backoffOffset) = (tokenUsed, ServerClock.Offset);
            Log.Warning("Profile sync backing off {Seconds:F0}s after failure #{Count} (status {Status})",
                wait.TotalSeconds, _backoffFailures, status?.ToString() ?? "none");
        }

        public PrizesFeed? Prizes { get; set; }
        /// <summary>Head notification after a successful authenticated receipt has queued its prize snapshot.</summary>
        public Action<string>? ReceiptReceived { get; set; }
        public bool Loaded { get; private set; }
        /// <summary>The account whose sync answer has been through the cloud-loadout fill (WPF: the sync precedes
        /// ProfileLoaded). The frame/avatar equip pull waits for it, so it never makes an empty loadout non-empty first.</summary>
        public string? CosmeticsAccount { get; private set; }
        /// <summary>Raised (on the sync's thread) the first time <see cref="CosmeticsAccount"/> becomes an account.</summary>
        public event Action? CosmeticsReady;
        internal int SessionVersion => Volatile.Read(ref _sessionVersion);
        /// <summary>The fuse fed by each successful response's <c>descent_countdown</c> block.</summary>
        public Descent.DescentCountdownService? Countdown { get; set; }
        public Descent.DescentService? DescentReader { get; set; }
        /// <summary>WPF DescentMigrationService.StageCeremonyDue: one queued stage released after a successful sync,
        /// at most once per local day (the companion carries the moment). Raised on the UI dispatcher.</summary>
        public Action<int>? StageCeremonyDue { get; set; }
        public DateTime? LastSyncTime { get; private set; }
        private int _syncSuccesses;
        internal Func<TimeSpan, Task> PurchaseRetryDelay = Task.Delay;
        /// <summary>Called when recovery changes the level or XP displayed by the head.</summary>
        public Action? RecoveredProgressionChanged { get; set; }

        /// <param name="localAchievements">This install's unlocked achievement ids.</param>
        /// <param name="inSession">The heartbeat's in_session.</param>
        /// <param name="handler">Test seam; null is the real network.</param>
        /// <param name="sanitizeCosmetics">The head's SanitizeOwn (registry + your unlocks); null never sends cosmetics.</param>
        public SyncPush(Func<IEnumerable<string>?> localAchievements, Func<bool> inSession, HttpMessageHandler? handler = null,
            Func<ProfileCosmetics?, ProfileCosmetics>? sanitizeCosmetics = null)
        {
            (_localAchievements, _inSession, _handler, _sanitizeCosmetics) = (localAchievements, inSession, handler, sanitizeCosmetics);
            _http = V2AuthService.Configure(new HttpClient(handler ?? new ServerClockHandler()));   // learns the server clock (release/6.11.5)
        }

        /// <summary>The profile load succeeded: the baseline pushes are allowed against.</summary>
        public void MarkLoaded(IEnumerable<string>? serverAchievements)
        {
            _serverAchievements = serverAchievements?.ToArray();
            Loaded = true;
            RestoreCloudAchievements(CoreSettings.Current, _serverAchievements, Volatile.Read(ref _sessionVersion), CoreSettings.Current.UnifiedId);
        }

        /// <summary>The head's silent union of cloud achievement ids into the local store.</summary>
        public Action<IEnumerable<string>>? RestoreAchievements { get; set; }

        // WPF ProfileSyncService.cs:2099-2118 (restack b164, #3712): the profile and every sync reply carry the
        // account's cloud achievements; union them locally on the UI dispatcher, never into another session.
        private void RestoreCloudAchievements(AppSettings s, IEnumerable<string>? ids, int session, string? account)
        {
            if (ids == null || RestoreAchievements == null) return;
            var cloud = ids.ToArray();
            if (cloud.Length == 0) return;
            _serverAchievements = (_serverAchievements ?? Array.Empty<string>()).Union(cloud).ToArray();
            CoreDispatch.Post(() =>
            {
                if (!Loaded || session != Volatile.Read(ref _sessionVersion) || !ReferenceEquals(s, CoreSettings.Current)
                    || !SignedIn(s) || s.UnifiedId != account) return;
                try { RestoreAchievements?.Invoke(cloud); }
                catch (Exception ex) { Log.Debug("V2 Sync: cloud achievement restore failed: {Error}", ex.Message); }
            });
        }

        // WPF ProfileSyncService.cs:2149-2165 (restack b164, #4120): the mobile quest ledger stays apart from the
        // desktop counters and takes the server's totals verbatim.
        internal static void AdoptMobileQuestTotals(AppSettings s, JObject reply)
        {
            if (reply["user"] is not JObject user || user["mobile_stats"] is not JObject mobile) return;
            static int Total(JObject o, string key) => o[key]?.Type == JTokenType.Integer ? (int)o[key]! : 0;
            s.MobileQuestDailyCompleted = Total(mobile, "total_daily_quests_completed");
            s.MobileQuestWeeklyCompleted = Total(mobile, "total_weekly_quests_completed");
            s.MobileQuestXP = Total(mobile, "total_xp_from_quests");
        }

        /// <summary>A profile read that carried these fields already took the higher value (WPF reads the
        /// profile before its first sync), so the next pushes may send them. Only companion progress and
        /// conditioning minutes are gated this way; a logout (<see cref="Reset"/>) disarms them again.</summary>
        public void NoteProgressRead(SyncBody.Field fields)
            => _knownProgress |= fields & (SyncBody.Field.CompanionProgress | SyncBody.Field.TotalConditioningMinutes);

        /// <summary>Logout: nothing is pushed until the next load.</summary>
        public void Reset()
        {
            StopHeartbeat();
            Prizes?.Clear();
            DescentReader?.Reset();
            Interlocked.Increment(ref _sessionVersion);
            Loaded = false;
            CosmeticsAccount = null;
            _adopted = false;
            _knownProgress = SyncBody.Field.None;
            _serverAchievements = null;
            _pendingCosmeticsClear = false;   // never carry one account's unequip into the next
            LastSyncTime = null;
        }

        private static bool SignedIn(AppSettings s) => !s.OfflineMode && !string.IsNullOrEmpty(s.UnifiedId) && CoreAccount.IsLoggedIn;

        /// <summary>The body: known fields only (achievements only when <paramref name="achievements"/> is known, never
        /// shrunk to local-only). Xp is the TOTAL, as WPF sends it.</summary>
        /// <param name="cosmetics">An explicit loadout save (WPF BuildCosmeticsPayload after a load: the sanitized
        /// loadout, the empty one included - that is the unequip-everything clear). Null leaves the key out.</param>
        public static SyncBody Body(AppSettings s, IEnumerable<string>? achievements, ProfileCosmetics? cosmetics = null, SyncBody.Field consent = SyncBody.Field.None, bool adopted = false, SyncBody.Field knownProgress = SyncBody.Field.None) => new()
        {
            Known = (achievements == null ? Sent & ~SyncBody.Field.Achievements : Sent)
                    | (cosmetics == null ? SyncBody.Field.None : SyncBody.Field.Cosmetics) | (consent & Privacy)
                    | SyncBody.Field.ResetDailyQuest | SyncBody.Field.ResetWeeklyQuest
                    | (adopted ? AfterAdopt : SyncBody.Field.None)
                    | (knownProgress & (SyncBody.Field.CompanionProgress | SyncBody.Field.TotalConditioningMinutes))
                    | (adopted && s.PendingSkillsResetAck ? SyncBody.Field.ForceSkillsReset : SyncBody.Field.None)
                    | (s.LastWebXpClaimId == null ? SyncBody.Field.None : SyncBody.Field.WebXpClaimAck),
            WebXpClaimAck = s.LastWebXpClaimId,
            CompanionProgress = s.CompanionProgressData,
            TotalConditioningMinutes = s.TotalConditioningMinutes,
            UnlockedSkills = s.UnlockedSkills?.ToList() ?? new List<string>(),
            SkillPoints = s.SkillPoints,
            ForceSkillsReset = false,
            UnifiedId = s.UnifiedId,
            Xp = (int)ProfileAdopt.TotalXp(s),
            Level = s.PlayerLevel,
            DescentEpoch = Descent.DescentEpochs.ClientEpoch,
            DescentAuto = true,
            Achievements = achievements?.Distinct().OrderBy(a => a, StringComparer.Ordinal).ToList(),
            Cosmetics = cosmetics,
            AllowDiscordDm = s.AllowDiscordDm,
            ShowOnlineStatus = s.ShowOnlineStatus,
            ShareProfilePicture = s.ShareProfilePicture,
            PublicShareAvatar = s.PublicShareRealAvatar,
            GoonShareAvatar = s.GoonShareAvatar,
            GoonShareDm = s.GoonShareDiscordDm,
        };

        /// <summary>WPF PersistOwnCosmetics' push, after <paramref name="chosen"/> was saved to settings. Every push
        /// sends the settings loadout (WPF BuildCosmeticsPayload), so a cooldown skip, a logout or a restart cannot
        /// lose it; an empty <paramref name="chosen"/> is the explicit clear, held until a sync delivers it.</summary>
        public Task<bool> PushCosmeticsAsync(ProfileCosmetics chosen)
        {
            _pendingCosmeticsClear = chosen.IsEmpty;
            return PushAsync("cosmetics");
        }

        /// <summary>WPF ProfileSyncService.BuildCosmeticsPayload: an empty loadout goes as an explicit clear
        /// or after adopting the cloud response this session. Before adoption, omit empty fresh-install defaults.
        /// Any failure leaves the key out ("no change" never destroys anything).</summary>
        private ProfileCosmetics? CosmeticsPayload(AppSettings s, bool clear)
        {
            if (_sanitizeCosmetics == null) return null;
            try
            {
                var clean = _sanitizeCosmetics(s.ProfileCosmetics);
                return !clean.IsEmpty || clear || _adopted ? clean : null;
            }
            catch (Exception ex) { Log.Debug("SyncPush cosmetics payload: {E}", ex.Message); return null; }
        }

        /// <param name="waitForGate">Logout: wait (bounded) for an in-flight push instead of skipping.</param>
        public Task<bool> PushAsync(string reason, bool waitForGate = false) => PushCoreAsync(reason, waitForGate, false);

        // WPF Login.cs:175-181: only the post-login fallback can try without a profile read.
        internal Task<bool> PushAfterFailedReadAsync() => PushCoreAsync("after login", false, true);

        private async Task<bool> PushCoreAsync(string reason, bool waitForGate, bool allowUnread)
        {
            var s = CoreSettings.Current;
            var session = Volatile.Read(ref _sessionVersion);
            if ((!Loaded && !allowUnread) || !SignedIn(s)) { Log.Debug("Profile sync skipped ({Reason}) - not loaded this session or not signed in", reason); return false; }
            if (!await _gate.WaitAsync(waitForGate ? TimeSpan.FromSeconds(5) : TimeSpan.Zero)) return false;
            try
            {
                if (LastSyncTime is { } last && UtcNow() - last < Cooldown) { Log.Debug("Profile sync skipped - cooldown active"); return false; }
                if (SyncFailureBackoff.ShouldSkip(UtcNow(), _blockedUntilUtc, _backoffToken, s.AuthToken, _backoffOffset, ServerClock.Offset))
                {
                    Log.Debug("Profile sync skipped - backing off after {Failures} failure(s)", _backoffFailures);
                    return false;
                }
                var settings = CoreSettings.Service;
                // A failed-read fallback never reconciles (it refuses a restored backup below).
                if (Loaded && settings?.RestoredFromBackup == true)
                {
                    var idBeforeRead = s.UnifiedId!;
                    var user = await new V2AuthService(() => s, _handler).GetUserProfileAsync(idBeforeRead);
                    if (user == null || !string.Equals(user.UnifiedId, idBeforeRead, StringComparison.Ordinal) || !Loaded || !SignedIn(s) || !ReferenceEquals(settings, CoreSettings.Service)
                        || !ReferenceEquals(s, settings.Current) || !string.Equals(idBeforeRead, s.UnifiedId, StringComparison.Ordinal)) return false;
                    var beforeLevel = s.PlayerLevel;
                    var beforeXp = s.PlayerXP;
                    RecoveredProfileReconcile.Apply(s, user, settings.RestoredSeason, UtcNow());
                    if (s.PlayerLevel != beforeLevel || Math.Abs(s.PlayerXP - beforeXp) >= 0.01)
                    {
                        try { RecoveredProgressionChanged?.Invoke(); }
                        catch (Exception ex) { Log.Warning(ex, "Recovered progression notification failed"); }
                    }
                    if (!settings.TrySaveImmediate(suppressCloudBackup: true)) return false;
                    settings.ClearRestoredFromBackupFlag();
                }
                var totalXp = ProfileAdopt.TotalXp(s);
                var unread = !Loaded;
                // WPF ProfileSyncService.cs:1473-1479,1513-1518: an unreconciled backup or
                // default-looking profile cannot be asserted to the server after a failed read.
                if (unread && (CoreSettings.Service?.RestoredFromBackup == true
                    || (s.PlayerLevel <= 1 && totalXp < ProfileAdopt.MeaningfulProgressXp)
                    || Descent.DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice))) return false;
                var watermark = Descent.DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice)
                    ? 0 : ProfileAdopt.ActiveXpWatermark(s);
                if (watermark > 0 && totalXp < watermark)
                {
                    Log.Error("[XP watermark] Sync REFUSED — would push {Xp} XP, below the {Watermark} XP last agreed", (int)totalXp, (int)watermark);
                    _hasSyncError = true;
                    ConsecutiveSyncFailures++;
                    SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
                    return false;
                }
                var id = s.UnifiedId!;
                if ((!Loaded && !allowUnread) || !SignedIn(s) || !ReferenceEquals(s, CoreSettings.Current)
                    || session != Volatile.Read(ref _sessionVersion)) return false;
                Prizes?.NoteAccount(id);
                var server = _serverAchievements;
                var clearing = !unread && _pendingCosmeticsClear;
                var cosmetics = unread ? null : CosmeticsPayload(s, clearing);
                string body;
                if (unread)
                {
                    // Unknown account fields must not be overwritten by local defaults.
                    var recovery = Body(s, null);
                    recovery.Known &= SyncBody.Field.UnifiedId | SyncBody.Field.Xp | SyncBody.Field.Level
                        | SyncBody.Field.DescentEpoch | SyncBody.Field.DescentAuto;
                    body = JsonConvert.SerializeObject(recovery);
                }
                else body = SerializeBody(s, server?.Concat(_localAchievements() ?? Array.Empty<string>()), cosmetics, ConsentFields(s), _adopted, _knownProgress);
                var tokenUsed = s.AuthToken;
                HttpRequestMessage NewRequest()
                {
                    var r = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/sync");
                    if (!string.IsNullOrEmpty(tokenUsed)) r.Headers.Add("X-Auth-Token", tokenUsed);
                    r.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    SyncBody.SignRequest(r, id, body);
                    return r;
                }
                Log.Information("Syncing profile ({Reason}) - Level: {Level}, TotalXP: {Xp}", reason, s.PlayerLevel, (int)totalXp);
                using var request = NewRequest();
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                // A 403 for clock skew: the signature was right, our timestamp was not. Learn the
                // server's time from the body and re-sign ONCE (release/6.11.5).
                if (response.StatusCode == HttpStatusCode.Forbidden
                    && string.Equals(ServerClock.ParseRefusal(json, out var serverTime), "clock_skew", StringComparison.OrdinalIgnoreCase))
                {
                    if (serverTime != null) ServerClock.Observe(serverTime, DateTimeOffset.UtcNow);
                    Log.Warning("V2 Profile sync refused for clock skew; re-signing with server offset {Seconds:F0}s and retrying once", ServerClock.Offset.TotalSeconds);
                    response.Dispose();
                    using var retry = NewRequest();
                    response = await _http.SendAsync(retry);
                    json = await response.Content.ReadAsStringAsync();
                }
                using var _ = response;
                // Signed out (or into another account) while this was in flight: the answer is not ours to apply.
                if ((!Loaded && !allowUnread) || !SignedIn(s) || !ReferenceEquals(s, CoreSettings.Current)
                    || session != Volatile.Read(ref _sessionVersion) || !string.Equals(s.UnifiedId, id, StringComparison.Ordinal))
                {
                    Log.Information("Profile sync response dropped - the account changed while it was in flight");
                    return false;
                }
                if (!response.IsSuccessStatusCode)
                {
                    // Contract D: merged tombstone; the head's recovery re-signs in and reloads.
                    if (V2AuthService.MergedRecovery is { } merged && await merged(response, json))
                    {
                        // WPF records the deferred merge as an error without starting backoff.
                        _hasSyncError = true;
                        ConsecutiveSyncFailures++;
                        SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
                        return false;
                    }
                    // 429, and a 409 no recovery handled: cool down rather than retry on every trigger.
                    if (response.StatusCode is (HttpStatusCode)429 or HttpStatusCode.Conflict)
                    {
                        // WPF retains LastSyncError on 429 and counts it in the finally block.
                        if (response.StatusCode == HttpStatusCode.Conflict || _hasSyncError)
                        {
                            _hasSyncError = true;
                            ConsecutiveSyncFailures++;
                            SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
                        }
                        LastSyncTime = UtcNow();
                        Log.Warning("V2 Profile sync refused by server ({Status}), will retry after the cooldown", (int)response.StatusCode);
                        return false;
                    }
                    Log.Warning("V2 Profile sync failed: {Status} (error body {Bytes} bytes)", (int)response.StatusCode, json.Length);
                    NoteFailureForBackoff((int)response.StatusCode, tokenUsed);
                    return false;
                }
                LastSyncTime = UtcNow();
                Interlocked.Increment(ref _syncSuccesses);
                (_backoffFailures, _blockedUntilUtc) = (0, null);
                _hasSyncError = false;
                if (ConsecutiveSyncFailures > 0)
                {
                    ConsecutiveSyncFailures = 0;
                    SyncHealthChanged?.Invoke(this, 0);
                }
                // The clear has reached the server; an empty loadout goes back to meaning "no change" (WPF).
                if (clearing && cosmetics?.IsEmpty == true) _pendingCosmeticsClear = false;
                Log.Information("V2 Profile synced successfully ({Bytes} bytes)", json.Length);
                int? stageDue = null;
                try
                {
                    var reply = JObject.Parse(json);
                    // WPF ProfileSyncService.cs:1874: ownership precedes other response flags.
                    try
                    {
                        Prizes?.Apply(id, id, reply["prizes"]?.ToObject<PrizesBlock>(), "V2 sync");
                        // #4811: Rain's look may also pull here, once a current-account snapshot is held (WPF pulls only after a validate).
                        ReceiptReceived?.Invoke(id);
                    }
                    catch (Exception ex) { Log.Debug("V2 Sync: prize receipt failed: {Error}", ex.Message); }
                    if (reply["user"] is JObject user && user["achievements"] is JArray achievements
                        && achievements.All(a => a.Type == JTokenType.String))
                        RestoreCloudAchievements(s, achievements.Values<string>().Select(a => a!), session, id);
                    AdoptMobileQuestTotals(s, reply);
                    if (AdoptFromResponse(s, reply, id)) _adopted = true;
                    // #3898: the loadout fill has run for this account (an answer that carries cosmetics, even null).
                    if (_sanitizeCosmetics != null
                        && (reply["cosmetics"] is JObject || reply["cosmetics"]?.Type == JTokenType.Null) && CosmeticsAccount != id)
                    {
                        CosmeticsAccount = id;
                        try { CosmeticsReady?.Invoke(); } catch (Exception ex) { Log.Debug("V2 Sync: CosmeticsReady handler threw: {Error}", ex.Message); }
                    }
                    ClaimWebXp(s, reply, allowUnread, session, id);
                    ApplyQuestResets(s, id, session, reply);
                    // Then the level/XP adopt (held while a migration is pending) and WPF's migration handshake
                    // (ProfileSyncService.cs:1903-1904): the ack first, the offer second. A taken offer syncs again.
                    if (ApplyReply(s, reply, DescentReader?.Current?.Stage, UtcNow(), PersistMigration))
                        Nudge("descent migration");
                    // WPF ProfileSyncService.cs:1915 -> DescentMigrationService.TickStageDrip (:390-412).
                    stageDue = Descent.DescentMigration.ReleaseStageCeremony(s, DateTime.Now);
                }
                catch (Exception ex) { Log.Debug("V2 Sync: Could not parse server flags: {Error}", ex.Message); }
                // THE FUSE's cache, off the RAW body (WPF ProfileSyncService.HandleDescentCountdown).
                if (Countdown != null && Descent.DescentCountdownService.TryReadCeremonyAt(json, out var ceremonyAt))
                    Countdown.ApplyCeremonyAt(ceremonyAt);
                if (DescentReader?.HasSeenBlock == true) DescentReader.RequestRefresh("profile synced");
                CoreSettings.Save();
                if (stageDue is { } due && StageCeremonyDue is { } speak)
                {
                    Log.Information("[Descent] Stage {Stage} ceremony released ({Left} still queued).", due, s.DescentPendingStageCeremonies.Count);
                    CoreDispatch.Post(() => { try { speak(due); } catch (Exception ex) { Log.Debug("[Descent] StageCeremonyDue handler threw: {Error}", ex.Message); } });
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Profile sync failed");
                // WPF ProfileSyncService: a shutdown cancellation is not a server failure, but HttpClient.Timeout
                // (a cancellation wrapping a TimeoutException) is a hung proxy and backs off like any other.
                if (!IsExpectedCancellation(ex) || ex.InnerException is TimeoutException)
                    NoteFailureForBackoff(null, s.AuthToken);
                return false;
            }
            finally { _gate.Release(); }
        }

        // WPF ProfileSyncService.cs:1791-1800. The queued action belongs to the request's
        // account and session, even if logout or another login happens before UI dispatch.
        private void ApplyQuestResets(AppSettings s, string account, int session, JObject reply)
        {
            bool daily = reply["reset_daily_quest"]?.Type == JTokenType.Boolean && reply.Value<bool>("reset_daily_quest");
            bool weekly = reply["reset_weekly_quest"]?.Type == JTokenType.Boolean && reply.Value<bool>("reset_weekly_quest");
            if (!daily && !weekly) return;
            CoreDispatch.Post(() =>
            {
                if (!Loaded || session != Volatile.Read(ref _sessionVersion) || !ReferenceEquals(s, CoreSettings.Current)
                    || !SignedIn(s) || s.UnifiedId != account) return;
                if (daily) ResetDailyQuest?.Invoke();
                if (weekly) ResetWeeklyQuest?.Invoke();
            });
        }

        private static void PersistMigration()
        {
            if (!CoreSettings.TrySaveImmediate()) throw new IOException("Migration checkpoint was not saved.");
        }

        internal static string SerializeBody(AppSettings s, IEnumerable<string>? achievements,
            ProfileCosmetics? cosmetics = null, SyncBody.Field consent = SyncBody.Field.None,
            bool adopted = false, SyncBody.Field knownProgress = SyncBody.Field.None)
        {
            var body = JsonConvert.SerializeObject(Body(s, achievements, cosmetics, consent, adopted, knownProgress));
            if (!Descent.DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice)) return body;
            var payload = JObject.Parse(body);
            payload["descent_migration"] = new JObject { ["choice"] = s.PendingDescentMigrationChoice };
            return payload.ToString(Formatting.None);
        }

        // All ordinary response handling precedes the local relevel. Until ack, the old server
        // ledger must not resurrect a pending migration, including after a process restart.
        internal static bool ApplyReply(AppSettings s, JObject reply, Descent.DescentStage? stage,
            DateTime now, Action persist)
        {
            var ack = reply["descent_migration"] is JObject b
                && b["completed"]?.Type == JTokenType.Boolean && b.Value<bool>("completed");
            if (!Descent.DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice) || ack)
                ProfileAdopt.ApplySyncResponse(s, reply, now);
            Descent.DescentMigrationAck.Apply(s, reply);
            return Descent.DescentAutoRestore.ApplyOffer(s, reply, stage, now, persist);
        }

        // WPF ProfileSyncService.cs:3964. Only successful syncs count, never a 429/409 cooldown stamp.
        public async Task<bool> SyncBeforeRetryAsync()
        {
            var landed = Volatile.Read(ref _syncSuccesses);
            if (!await _gate.WaitAsync(Cooldown)) return false;
            _gate.Release();
            if (Volatile.Read(ref _syncSuccesses) != landed || await PushAsync("purchase-retry")) return true;
            var left = LastSyncTime is { } last ? Cooldown - (UtcNow() - last) : TimeSpan.Zero;
            if (left <= TimeSpan.Zero || left > Cooldown) return false;
            await PurchaseRetryDelay(left + TimeSpan.FromMilliseconds(250));
            return await PushAsync("purchase-retry");
        }

        /// <summary>
        /// WPF SyncProfileAsync:1897-1954, in its order: the force_skills_reset (clear the tree, take the server's points,
        /// or level x PointsPerLevel; ack pending until a response no longer carries the flag), else skill points
        /// wallet-net spend adoption (V2WalletNet, the named lower rule) or MergeMax; the unlocked-skills union (skipped on level_reset, whose own branch unions); then the cloud loadout
        /// into an EMPTY local one only (WPF AdoptCloudCosmetics). Does not save. True when the response carried both
        /// skill_points and unlocked_skills, i.e. the server's skills are now known.
        /// </summary>
        internal bool AdoptFromResponse(AppSettings s, JObject reply, string? syncAccount = null)
        {
            bool Flag(string k) => reply[k]?.Type == JTokenType.Boolean && reply.Value<bool>(k);
            int? points = reply["skill_points"]?.Type == JTokenType.Integer ? reply.Value<int>("skill_points") : null;
            var forceReset = Flag("force_skills_reset");
            if (forceReset && !s.PendingSkillsResetAck)
            {
                var refunded = points ?? s.PlayerLevel * PointsPerLevel;
                Log.Information("V2 Sync: Force skills reset - clearing {Count} skills, setting points to {Points}", s.UnlockedSkills?.Count ?? 0, refunded);
                s.UnlockedSkills = new List<string>();
                s.SkillPoints = refunded;
                s.PendingSkillsResetAck = true;
            }
            else if (s.PendingSkillsResetAck && !forceReset)
                s.PendingSkillsResetAck = false;   // the server flag was cleared by our ack
            else if (points is int p)
            {
                // WPF V2WalletNet (#3834): a wallet_net drop adopts a spend made on another device; else raise-only.
                var live = CoreSettings.Current; var account = syncAccount ?? s.UnifiedId;
                if (!string.IsNullOrEmpty(account))
                {
                    var same = string.Equals(live.UnifiedId, account, StringComparison.Ordinal);
                    int? seen = live.WalletNetSeen.TryGetValue(account, out var stored) ? stored : null;
                    var step = global::ConditioningControlPanel.Services.Prizes.V2WalletNet.Decide(same, global::ConditioningControlPanel.Services.Prizes.V2WalletNet.ParseNet(reply["wallet_net"]), seen, p, live.SkillPoints);
                    if (step.NextSp != live.SkillPoints)
                    {
                        Log.Information("V2 Sync: Skill points server={Server}, local={Local} - now {Next}", p, live.SkillPoints, step.NextSp);
                        live.SkillPoints = step.NextSp;
                    }
                    if (same && step.StoreNet is { } net && seen != net)
                        live.WalletNetSeen = new Dictionary<string, int>(live.WalletNetSeen) { [account] = net };
                }
            }
            var skills = reply["unlocked_skills"] as JArray;
            if (skills is { Count: > 0 } && !Flag("level_reset"))
            {
                var local = s.UnlockedSkills ?? new List<string>();
                var add = skills.Values<string>().OfType<string>().Where(k => k.Length > 0 && !local.Contains(k)).Distinct().ToList();
                if (add.Count > 0)
                {
                    Log.Information("V2 Sync: Adding {Count} unlocked skills from server: {Skills}", add.Count, string.Join(", ", add));
                    s.UnlockedSkills = local.Concat(add).ToList();
                }
            }
            try
            {
                // WPF AdoptCloudCosmetics (ProfileSyncService.cs:2845): an unequip still owed to /v2/equip means the
                // echo's frame and avatar are what the player just took off.
                if (_sanitizeCosmetics != null && reply["cosmetics"] is JObject c && s.ProfileCosmetics.IsEmpty && !s.ProfileEquipPushOwed)
                {
                    var clean = _sanitizeCosmetics(c.ToObject<ProfileCosmetics>());
                    if (!clean.IsEmpty)
                    {
                        s.ProfileCosmetics = clean;
                        Log.Information("Adopted cloud profile cosmetics (banner {Banner}, accent {Accent}, {Pins} pinned) into an empty local loadout",
                            clean.BannerId ?? "none", clean.Accent ?? "none", clean.PinnedAchievements.Count);
                    }
                }
            }
            catch (Exception ex) { Log.Debug("AdoptCloudCosmetics skipped: {E}", ex.Message); }
            // WPF ProfileSyncService:2175-2231: time takes higher; a companion's whole record
            // is selected by level, then lifetime XP. Missing fields never arm a defaults push.
            // Own try like the cosmetics block: a malformed record must not skip ApplySyncResponse/ClaimWebXp.
            try
            {
                if (reply["total_conditioning_minutes"] is { Type: JTokenType.Integer or JTokenType.Float } minutes)
                {
                    // Take-higher through ProfileAdopt, whose ConditioningMinutesLifted the head rebaselines on.
                    if (ProfileAdopt.AdoptConditioningMinutes(s, minutes.Value<double>())) LifetimeLifted?.Invoke();
                    _knownProgress |= SyncBody.Field.TotalConditioningMinutes;
                }
                if (reply["companion_progress"] is JObject companions)
                {
                    foreach (var entry in companions.Properties())
                    {
                        if (!int.TryParse(entry.Name, out var id) || entry.Value is not JObject record) continue;
                        var server = record.ToObject<CompanionProgress>();
                        if (server == null) continue;
                        s.CompanionProgressData.TryGetValue(id, out var local);
                        if (local == null || server.Level > local.Level
                            || server.Level == local.Level && server.TotalXPEarned > local.TotalXPEarned)
                            s.CompanionProgressData[id] = server;
                    }
                    _knownProgress |= SyncBody.Field.CompanionProgress;
                }
            }
            catch (Exception ex) { Log.Debug("Companion/time merge skipped: {E}", ex.Message); }
            return points != null && skills != null;
        }

        /// <summary>WPF SyncProfileAsync:1970-1985: a new web XP claim is persisted (and saved) BEFORE its XP is added, so a
        /// crash under-pays once instead of paying twice; skipped on level_reset, whose level/XP overwrite would eat it. The
        /// id then goes back as <c>web_xp_claim_ack</c> on every push.</summary>
        internal void ClaimWebXp(AppSettings s, JObject reply, bool allowUnread = false, int? responseSession = null, string? responseAccount = null)
        {
            if (reply["web_xp"]?["claim"] is not JObject claim || reply["level_reset"]?.Type == JTokenType.Boolean && reply.Value<bool>("level_reset")) return;
            var id = claim["id"]?.Type == JTokenType.String ? (string?)claim["id"] : null;
            var amount = claim["amount"]?.Type == JTokenType.Integer ? (int)claim["amount"]! : 0;
            if (string.IsNullOrEmpty(id) || amount <= 0 || id == s.LastWebXpClaimId) return;
            var account = responseAccount ?? s.UnifiedId;
            var session = responseSession ?? Volatile.Read(ref _sessionVersion);
            CoreDispatch.Post(() =>
            {
                // WPF ProfileSyncService:1970-1985 pays immediately. Keep its id/save/payment together on UI dispatch.
                if ((!Loaded && !allowUnread) || session != Volatile.Read(ref _sessionVersion) || !ReferenceEquals(s, CoreSettings.Current)
                    || !SignedIn(s) || s.UnifiedId != account || s.LastWebXpClaimId == id) return;
                s.LastWebXpClaimId = id;
                CoreSettings.Save();
                ProgressionBank.AddClaimed(amount);
            });
        }

        /// <summary>WPF SkillTreeService.PointsPerLevel (the force-reset refund when the server sends no balance).</summary>
        private const int PointsPerLevel = 1;

        /// <summary>WPF NudgeSyncSoon: one coalesced push, 3 s out or just past the cooldown.</summary>
        public void Nudge(string reason)
        {
            if (!Loaded) return;
            var s = CoreSettings.Current;
            var account = s.UnifiedId;
            var session = Volatile.Read(ref _sessionVersion);
            var ticket = unchecked(session + 1);
            if (Interlocked.Exchange(ref _nudgePending, ticket) == ticket) return;
            var now = UtcNow();
            var wait = NudgeSettle;
            if (LastSyncTime is { } last && now - last < Cooldown)
                wait = Cooldown - (now - last) + NudgeCooldownSlack;
            if (SyncFailureBackoff.ShouldSkip(now, _blockedUntilUtc, _backoffToken, s.AuthToken, _backoffOffset, ServerClock.Offset)
                && _blockedUntilUtc is { } blocked && blocked - now + NudgeCooldownSlack > wait)
                wait = blocked - now + NudgeCooldownSlack;
            _ = Task.Delay(wait).ContinueWith(async _ =>
            {
                Interlocked.CompareExchange(ref _nudgePending, 0, ticket);
                if (session != Volatile.Read(ref _sessionVersion) || !ReferenceEquals(s, CoreSettings.Current)
                    || s.UnifiedId != account) return;
                await PushAsync(reason);
            }, TaskScheduler.Default);
        }

        /// <summary>A heartbeat reply's body, off the UI thread. It carries the server's effective_tier
        /// (WPF ProfileSyncService hands the same body to EntitlementTierSync.Offer).</summary>
        public Action<string>? HeartbeatBody;

        /// <summary>The response with the identity used for the request. Never log the token.</summary>
        public Action<string, string?, string>? HeartbeatIdentityBody;

        /// <summary>WPF StartHeartbeat: one now, then every 120 s.</summary>
        public void StartHeartbeat()
        {
            DescentReader?.OnSignedIn();
            if (_heartbeat != null) return;
            _heartbeat = new Timer(_ => _ = HeartbeatAsync(), null, TimeSpan.Zero, HeartbeatInterval);
            Log.Information("Heartbeat started (every {Seconds}s)", HeartbeatInterval.TotalSeconds);
        }

        internal bool HeartbeatRunning => Volatile.Read(ref _heartbeat) != null;

        public void StopHeartbeat()
        {
            Interlocked.Exchange(ref _heartbeat, null)?.Dispose();
        }

        /// <summary>WPF's four fields (Core <see cref="SyncBody.Heartbeat"/>); is_active true, as WPF with no idle tracker.</summary>
        public Task<bool> HeartbeatAsync()
        {
            var s = CoreSettings.Current;
            if (!SignedIn(s)) return Task.FromResult(false);
            var id = s.UnifiedId!;
            var token = s.AuthToken;
            return new V2AuthService(() => s, _handler).SendHeartbeatAsync(id, true, _inSession(), body =>
            {
                HeartbeatBody?.Invoke(body);
                HeartbeatIdentityBody?.Invoke(id, token, body);
            });
        }
    }
}
