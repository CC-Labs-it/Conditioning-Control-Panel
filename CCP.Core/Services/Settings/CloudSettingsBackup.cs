using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The cloud settings backup for a head that is not WPF: "Backup now" and "Restore from cloud"
    /// (WPF ProfileSyncService.cs:4545-4890 + MainWindow.CloudBackup.cs). Same endpoints and bodies.
    /// <para>The exclusion list, <see cref="IsExcludedFromBackup"/> and <see cref="PreserveLocalOnlyFields"/>
    /// follow WPF, with the old port lane's stricter secret and safety filtering. WPF keeps its own (P118), so this class has a new
    /// name (P25). <see cref="SettingsBackupBudget"/> moved here as is.</para>
    /// <para>On top of WPF (docs/avalonia-decisions.html, cloud settings backup): <see cref="AbsolutePathFields"/>
    /// are never uploaded and a restore always keeps this machine's values, because one account has one
    /// backup slot shared by Windows and Linux and an absolute path from one OS means nothing on the other.</para>
    /// </summary>
    public sealed class CloudSettingsBackup
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        // One client for the process: the section builds a CloudSettingsBackup on every refresh.
        private static readonly Lazy<HttpClient> Shared = new(() => V2AuthService.Configure(new HttpClient()));
        private readonly HttpClient _http;

        /// <param name="handler">Tests only; null is the real network.</param>
        public CloudSettingsBackup(HttpMessageHandler? handler = null) =>
            _http = handler == null ? Shared.Value : V2AuthService.Configure(new HttpClient(handler));

        /// <summary>WPF ProfileSyncService.cs:4257-4325: GDPR export, including one retry
        /// after genuine auth recovery. The caller saves the pretty JSON only after consent.</summary>
        public async Task<(bool success, string? error, string? jsonData)> ExportDataAsync(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.UnifiedId))
                return (false, Localization.Loc.Get("msg_export_login_required"), null);
            try
            {
                using var first = await PostAsync("/v2/user/export-data", settings, new { unified_id = settings.UnifiedId });
                var response = first;
                var json = await response.Content.ReadAsStringAsync();
                HttpResponseMessage? retry = null;
                try
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if (V2AuthService.MergedRecovery is { } merged && await merged(response, json))
                            return (false, Localization.Loc.Get("account_merged_retry_hint"), null);
                        if (await AuthRecovery.HandleUnauthorizedAsync(response) && !string.IsNullOrEmpty(settings.AuthToken))
                        {
                            retry = await PostAsync("/v2/user/export-data", settings, new { unified_id = settings.UnifiedId });
                            response = retry;
                            json = await response.Content.ReadAsStringAsync();
                        }
                    }
                    if (!response.IsSuccessStatusCode)
                        return (false, (string?)JsonConvert.DeserializeObject<JObject>(json)?["error"] ?? Localization.Loc.GetF("msg_export_server_error_0", response.StatusCode), null);
                    return (true, null, JToken.Parse(json).ToString(Formatting.Indented));
                }
                finally { retry?.Dispose(); }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Data export request failed");
                return (false, Localization.Loc.Get("msg_export_connection_required"), null);
            }
        }

        // ---- copied unchanged from WPF ProfileSyncService.cs:4553-4635 ----

        /// <summary>
        /// Properties to exclude from settings backup (server-authoritative or identity fields).
        /// </summary>
        private static readonly HashSet<string> ExcludedBackupProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(AppSettings.UnifiedId),
            nameof(AppSettings.OpenRouterApiKey),
            nameof(AppSettings.PlayerLevel),
            nameof(AppSettings.PlayerXP),
            nameof(AppSettings.SkillPoints),
            nameof(AppSettings.UnlockedSkills),
            nameof(AppSettings.HighestLevelEver),
            nameof(AppSettings.IsSeason0Og),
            nameof(AppSettings.CurrentSeason),
            nameof(AppSettings.PendingSkillsResetAck),
            nameof(AppSettings.UserDisplayName),
            nameof(AppSettings.PatreonTier),
            nameof(AppSettings.PatreonPremiumValidUntil),
            nameof(AppSettings.PatreonLabValidUntil),
            nameof(AppSettings.InviteGrantUntil),
            nameof(AppSettings.LastPatreonVerification),
            nameof(AppSettings.AuthToken),
            nameof(AppSettings.CustomAssetsPath),
            nameof(AppSettings.DiscordWebhookUrl),
            nameof(AppSettings.LastSeenUtc), // Local-only greeting timestamp — must never leave the device.
            // Documented local only: a restore on another PC must not switch presence sharing on.
            nameof(AppSettings.FriendsPresenceShared),
            // Same reason: a restore must not switch the always-on wallpaper on without its warning.
            nameof(AppSettings.CollageRunWithCcp),
            // A path on this PC: means nothing elsewhere, and names the user's folders.
            nameof(AppSettings.CollageOwnImagePath),
            // WPF 7.2.3: the #692 wallpaper breadcrumb and its fit, this PC's desktop until it is put back.
            nameof(AppSettings.WallpaperOriginalPath),
            nameof(AppSettings.WallpaperOriginalStyle),
            nameof(AppSettings.ModPersonalityPreset),
            // Conversion rows (C9): machine-local stamps and the last server tier reading.
            // A restore must not bring back a stale reading or re-open a once-ever row.
            nameof(AppSettings.ServerTierSeen),
            nameof(AppSettings.GraceClosedTier),
            nameof(AppSettings.GraceClosedAtUtc),
            nameof(AppSettings.InboxStamps),
            nameof(AppSettings.LastKnownEffectiveTier),
            // Free member fields mirror the server profile; the next profile read refills them.
            nameof(AppSettings.PatreonMember),
            nameof(AppSettings.PostGrants),
            nameof(AppSettings.ServerPaidUntil),
            // Names a baked Rain clip pack folder on this PC; another PC has no such folder.
            nameof(AppSettings.RainPackHash),
            // Frame and avatar equip state: what THIS PC owes the server and last saw of it.
            nameof(AppSettings.ProfileEquipAccount),
            nameof(AppSettings.ProfileEquipUpdatedMs),
            nameof(AppSettings.ProfileEquipPushOwed),
            // Rain look sync state: what THIS PC owes the server and last agreed with it (WPF ProfileSyncService).
            nameof(AppSettings.RainLookPushOwed),
            nameof(AppSettings.RainLookAgreedUtc),
            // What THIS PC last saw of each account's wallet net: on another PC it would invent or hide a spend.
            nameof(AppSettings.WalletNetSeen),
            // The one-time "online media is official" note: this PC saw it, not the account.
            nameof(AppSettings.ScrolllerOfficialNoteSeen),
            // ---- not in WPF (no WPF reader): which privacy switches this install owns (SyncPush) ----
            nameof(AppSettings.ConsentOwnedAccount),
            nameof(AppSettings.ConsentOwnedFields),
        };

        /// <summary>
        /// Every Chaster setting (the master switch, the prices, the lock, both limits and their
        /// waiting raises, pause, relock, consent, the ladder name). Device-local like the link itself:
        /// a cloud restore must never switch the tab on, add prices, or land a limit raise without its
        /// 24 hour wait. Found by name so a new Chaster* setting is covered the day it is added.
        /// </summary>
        internal static readonly System.Reflection.PropertyInfo[] ChasterLocalProperties =
            typeof(AppSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.Name.StartsWith("Chaster", StringComparison.Ordinal)
                            && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                            && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)))
                .ToArray();

        private static readonly string[] SecretMarkers = { "Token", "ApiKey", "Secret", "Password", "Webhook", "Credential" };

        /// <summary>True when a backup must not carry this property.</summary>
        internal static bool IsExcludedFromBackup(string name) =>
            ExcludedBackupProperties.Contains(name)
            || SecretMarkers.Any(marker => name != null && name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            || (name != null && name.StartsWith("Chaster", StringComparison.OrdinalIgnoreCase))
            // The audio player's playlist is file paths on this machine: never uploaded.
            || (name != null && name.StartsWith("AudioPlaylist", StringComparison.OrdinalIgnoreCase));

        /// <summary>The audio player's settings (playlist paths, volume, loop, shuffle), found by name.</summary>
        internal static readonly System.Reflection.PropertyInfo[] AudioPlaylistLocalProperties =
            typeof(AppSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.Name.StartsWith("AudioPlaylist", StringComparison.Ordinal)
                            && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                            && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)))
                .ToArray();

        /// <summary>
        /// The other half of <see cref="ExcludedBackupProperties"/>. A backup never carries these,
        /// so a restored settings object arrives with them at their defaults - and until 6.9.4
        /// both restore paths (the startup welcome-back sheet and the manual button on the
        /// Settings tab) let those defaults win. The content folder was the visible casualty:
        /// after a restore <c>CustomAssetsPath</c> read "", the assets prompt had already been
        /// spent by the first run, and nothing re-asked, so the app quietly fell back to the
        /// default folder. Identity and progression fields are copied by the callers themselves;
        /// this is for the machine-local settings that are nobody's progress but still the user's.
        /// </summary>
        internal static void PreserveLocalOnlyFields(AppSettings current, AppSettings restored)
        {
            if (current == null || restored == null) return;
            restored.CustomAssetsPath = current.CustomAssetsPath;
            restored.DiscordWebhookUrl = current.DiscordWebhookUrl;
            restored.LastSeenUtc = current.LastSeenUtc;
            restored.FriendsPresenceShared = current.FriendsPresenceShared;
            restored.CollageRunWithCcp = current.CollageRunWithCcp;
            restored.CollageOwnImagePath = current.CollageOwnImagePath;
            restored.WallpaperOriginalPath = current.WallpaperOriginalPath;
            restored.WallpaperOriginalStyle = current.WallpaperOriginalStyle;
            // Conversion state lives on this PC only (it is left out of the backup): a restore must
            // not wipe it, or the once-ever rows post again and a post-code grant goes missing.
            restored.InboxStamps = current.InboxStamps;
            restored.ServerTierSeen = current.ServerTierSeen;
            restored.LastKnownEffectiveTier = current.LastKnownEffectiveTier;
            restored.GraceClosedTier = current.GraceClosedTier;
            restored.GraceClosedAtUtc = current.GraceClosedAtUtc;
            restored.PatreonMember = current.PatreonMember;
            restored.PostGrants = current.PostGrants;
            restored.ServerPaidUntil = current.ServerPaidUntil;
            restored.ModPersonalityPreset = current.ModPersonalityPreset; // the setter copies
            restored.ScrolllerOfficialNoteSeen = current.ScrolllerOfficialNoteSeen;
            restored.RainPackHash = current.RainPackHash;
            restored.ProfileEquipAccount = current.ProfileEquipAccount;
            restored.ProfileEquipUpdatedMs = current.ProfileEquipUpdatedMs;
            restored.ProfileEquipPushOwed = current.ProfileEquipPushOwed;
            restored.RainLookPushOwed = current.RainLookPushOwed;
            restored.RainLookAgreedUtc = current.RainLookAgreedUtc;
            restored.WalletNetSeen = current.WalletNetSeen;
            restored.ConsentOwnedAccount = current.ConsentOwnedAccount;
            restored.ConsentOwnedFields = current.ConsentOwnedFields;
            // A backup over budget leaves out the per-file asset lists (SettingsBackupBudget), so
            // an empty list in the restore may just mean "not carried": keep this PC's own.
            if (restored.DisabledAssetPaths.Count == 0 && current.DisabledAssetPaths.Count > 0)
                restored.DisabledAssetPaths = new HashSet<string>(current.DisabledAssetPaths);
            if (restored.ActiveAssetPaths.Count == 0 && current.ActiveAssetPaths.Count > 0)
                restored.ActiveAssetPaths = new HashSet<string>(current.ActiveAssetPaths);
            if (restored.AssetPresets.Count == 0 && current.AssetPresets.Count > 0)
                restored.AssetPresets = new List<AssetPreset>(current.AssetPresets);
            foreach (var p in ChasterLocalProperties.Concat(AudioPlaylistLocalProperties))
            {
                var v = p.GetValue(current);
                if (v is List<string> list) v = new List<string>(list);
                p.SetValue(restored, v);
            }
        }

        // ---- new: absolute paths are machine-local ----

        /// <summary>Whole machine-local path settings. Portable lists retain their non-path fields;
        /// rooted item paths stay local on restore and carry existing slot paths on upload; relative paths travel.</summary>
        public static readonly IReadOnlyList<string> AbsolutePathFields = new[]
        {
            nameof(AppSettings.CustomAssetsPath), nameof(AppSettings.StartupVideoPath), nameof(AppSettings.SpiralPath),
            nameof(AppSettings.MindWipeAudioPath), nameof(AppSettings.WallpaperOriginalPath), nameof(AppSettings.WallpaperSourceFolder),
            nameof(AppSettings.BlinkTrainerFolders), nameof(AppSettings.DeeperRecentFiles), nameof(AppSettings.DeeperLastDirectory),
        };

        private static readonly System.Reflection.PropertyInfo[] PathProperties =
            AbsolutePathFields.Select(n => typeof(AppSettings).GetProperty(n)!).ToArray();

        /// <summary>WPF ProfileSyncService.cs:4561-4570 carries portable list fields. Strip rooted item paths here
        /// for cross-OS use; CarryPathFields restores the slot's paths for frozen WPF.</summary>
        internal static JObject StripForUpload(AppSettings settings)
        {
            var obj = JObject.Parse(JsonConvert.SerializeObject(settings, Formatting.None));
            foreach (var key in obj.Properties().Select(p => p.Name).ToList())
            {
                // The lists hold C# names; a [JsonProperty] key (server_tier_seen, inbox_stamps) is matched
                // through the property it serializes, the bare key too (case-insensitive).
                var name = PropertyNameOfJsonKey(key);
                if (IsExcludedFromBackup(key) || IsExcludedFromBackup(name)
                    || AbsolutePathFields.Contains(key, StringComparer.OrdinalIgnoreCase)
                    || AbsolutePathFields.Contains(name, StringComparer.OrdinalIgnoreCase))
                    obj.Remove(key);
            }
            foreach (var item in Items(obj, nameof(AppSettings.AudioLayers)))
            {
                DropRooted(item, nameof(AudioLayerTrack.Path));
                Drop(item, nameof(AudioLayerTrack.DisplayName)); // computed from Path, not persisted meaningfully
            }
            foreach (var item in Items(obj, nameof(AppSettings.CornerGifOverlays)))
                DropRooted(item, nameof(CornerGifOverlaySetting.GifPath));
            var triggers = Items(obj, nameof(AppSettings.KeywordTriggers))
                .Concat(Items(obj, nameof(AppSettings.KeywordTriggerPresets)).SelectMany(p => Items(p, "triggers")));
            foreach (var item in triggers)
            {
                DropRooted(item, nameof(KeywordTrigger.AudioFilePath)); // legacy dispatch fallback
                foreach (var action in Items(item, nameof(KeywordTrigger.Actions)))
                    if (string.Equals((string?)action.GetValue("type", StringComparison.OrdinalIgnoreCase), "PlayAudio", StringComparison.OrdinalIgnoreCase))
                        DropRooted(action, "filePath");
            }
            return obj;
        }

        // Case-insensitive: a serializer naming policy must not let a path slip through.
        private static IEnumerable<JObject> Items(JObject? o, string list) =>
            (o?.GetValue(list, StringComparison.OrdinalIgnoreCase) as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
        private static void Drop(JObject o, string name) => o.Property(name, StringComparison.OrdinalIgnoreCase)?.Remove();


        // OS-independent: a Windows drive or rooted backslash is local even on Linux.
        private static bool IsRootedPath(string? path) => !string.IsNullOrEmpty(path)
            && (path[0] is '/' or '\\' || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'));

        private static void DropRooted(JObject item, string name)
        {
            if (IsRootedPath((string?)item.GetValue(name, StringComparison.OrdinalIgnoreCase))) Drop(item, name);
        }

        private static JObject? ItemAt(JObject? parent, string list, int index) =>
            (parent?.GetValue(list, StringComparison.OrdinalIgnoreCase) as JArray)?.ElementAtOrDefault(index) as JObject;

        // Relative values, including an explicit empty path, travel. A stripped/omitted path
        // or a foreign rooted path takes the matched local path, or blank when unmatched.
        private static string KeepPath(JObject? item, string name, string? local)
        {
            var path = (string?)item?.GetValue(name, StringComparison.OrdinalIgnoreCase);
            return path != null && !IsRootedPath(path) ? path : local ?? "";
        }

        private static readonly Lazy<Dictionary<string, string>> JsonKeyToProperty = new(() =>
            ((Newtonsoft.Json.Serialization.JsonObjectContract)JsonSerializer.CreateDefault().ContractResolver
                .ResolveContract(typeof(AppSettings))).Properties
                .Where(p => p.PropertyName != null && p.UnderlyingName != null)
                .GroupBy(p => p.PropertyName!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().UnderlyingName!, StringComparer.Ordinal));

        /// <summary>The C# property a JSON key of <see cref="AppSettings"/> serializes; the key itself when unknown.</summary>
        internal static string PropertyNameOfJsonKey(string key)
            => JsonKeyToProperty.Value.TryGetValue(key, out var name) ? name : key;

        /// <summary>
        /// What a restore keeps from this machine: WPF's manual-restore identity/progression list
        /// (MainWindow.CloudBackup.cs:138-157), <see cref="PreserveLocalOnlyFields"/>, then every
        /// <see cref="AbsolutePathFields"/> value and matched list-item paths.
        /// <para>The manual restore runs this a second time after its awaited download (#3294): that pass has no
        /// backup JSON and skips the list-item paths, which <see cref="DownloadAsync"/> already matched.</para>
        /// </summary>
        internal static void KeepLocal(AppSettings current, AppSettings restored) => KeepLocal(current, restored, null);

        internal static void KeepLocal(AppSettings current, AppSettings restored, JObject? backup)
        {
            restored.UnifiedId = current.UnifiedId;
            restored.PlayerLevel = current.PlayerLevel;
            restored.PlayerXP = current.PlayerXP;
            restored.SkillPoints = current.SkillPoints;
            restored.UnlockedSkills = current.UnlockedSkills;
            restored.HighestLevelEver = current.HighestLevelEver;
            restored.IsSeason0Og = current.IsSeason0Og;
            restored.CurrentSeason = current.CurrentSeason;
            restored.PendingSkillsResetAck = current.PendingSkillsResetAck;
            restored.UserDisplayName = current.UserDisplayName;
            restored.PatreonTier = current.PatreonTier;
            restored.PatreonPremiumValidUntil = current.PatreonPremiumValidUntil;
            restored.PatreonLabValidUntil = current.PatreonLabValidUntil;
            restored.InviteGrantUntil = current.InviteGrantUntil;
            restored.LastPatreonVerification = current.LastPatreonVerification;
            restored.OpenRouterApiKey = current.OpenRouterApiKey;
            PreserveLocalOnlyFields(current, restored);
            foreach (var p in PathProperties)
            {
                var v = p.GetValue(current);
                if (v is List<string> list) v = new List<string>(list);
                p.SetValue(restored, v);
            }
            if (backup != null) KeepListPaths(current, restored, backup);
        }

        private static void KeepListPaths(AppSettings current, AppSettings restored, JObject backup)
        {
            // No stable id/name on tracks or corner slots: their persisted order is the key.
            // Check JSON presence: a missing old list keeps local items; an explicit [] clears it.
            bool Missing(string name) => backup.GetValue(name, StringComparison.OrdinalIgnoreCase) == null;
            if (Missing(nameof(AppSettings.AudioLayers))) restored.AudioLayers = new(current.AudioLayers);
            else for (var i = 0; i < restored.AudioLayers.Count; i++)
                restored.AudioLayers[i].Path = KeepPath(ItemAt(backup, nameof(AppSettings.AudioLayers), i),
                    nameof(AudioLayerTrack.Path), current.AudioLayers.ElementAtOrDefault(i)?.Path);
            if (Missing(nameof(AppSettings.CornerGifOverlays))) restored.CornerGifOverlays = new(current.CornerGifOverlays);
            else for (var i = 0; i < restored.CornerGifOverlays.Count; i++)
                restored.CornerGifOverlays[i].GifPath = KeepPath(ItemAt(backup, nameof(AppSettings.CornerGifOverlays), i),
                    nameof(CornerGifOverlaySetting.GifPath), current.CornerGifOverlays.ElementAtOrDefault(i)?.GifPath);
            if (Missing(nameof(AppSettings.KeywordTriggers))) restored.KeywordTriggers = new(current.KeywordTriggers);
            else RestoreTriggerPaths(restored.KeywordTriggers, current.KeywordTriggers, Items(backup, nameof(AppSettings.KeywordTriggers)).ToList());
            if (Missing(nameof(AppSettings.KeywordTriggerPresets))) restored.KeywordTriggerPresets = new(current.KeywordTriggerPresets);
            else for (var i = 0; i < restored.KeywordTriggerPresets.Count; i++)
                RestoreTriggerPaths(restored.KeywordTriggerPresets[i].Triggers ??= new(),
                    ById(restored.KeywordTriggerPresets, i, current.KeywordTriggerPresets, p => p.Id)?.Triggers ?? new(),
                    Items(ItemAt(backup, nameof(AppSettings.KeywordTriggerPresets), i), "triggers").ToList());
        }

        private static void RestoreTriggerPaths(List<KeywordTrigger> restored, List<KeywordTrigger> current, List<JObject> json)
        {
            for (var i = 0; i < restored.Count; i++)
            {
                var trigger = restored[i];
                var local = ById(restored, i, current, t => t.Id);
                var source = json.ElementAtOrDefault(i);
                trigger.AudioFilePath = KeepPath(source, nameof(KeywordTrigger.AudioFilePath), local?.AudioFilePath);
                // Action ids are not stable (every edit runs RebuildActionsFromFlatFields, which mints new
                // ones), so a miss falls back to the local PlayAudio at the same position, then AudioFilePath.
                var localAudio = local?.Actions.OfType<PlayAudioAction>().ToList() ?? new();
                var restoredAudio = trigger.Actions.OfType<PlayAudioAction>().ToList();
                var jsonAudio = Items(source, nameof(KeywordTrigger.Actions)).Where(a =>
                    string.Equals((string?)a.GetValue("type", StringComparison.OrdinalIgnoreCase), "PlayAudio", StringComparison.OrdinalIgnoreCase)).ToList();
                for (var j = 0; j < restoredAudio.Count; j++)
                    restoredAudio[j].FilePath = KeepPath(jsonAudio.ElementAtOrDefault(j), "filePath", local == null ? null
                        : (ById(restoredAudio, j, localAudio, a => a.Id) ?? localAudio.ElementAtOrDefault(j))?.FilePath
                          ?? local.AudioFilePath);
            }
        }

        /// <summary>The local twin of restored[i] by id. A duplicated id is ambiguous on either side, so it
        /// only matches the local item at the same position carrying the same id.</summary>
        private static T? ById<T>(IList<T> restored, int i, IList<T> local, Func<T, string?> id) where T : class
        {
            var key = id(restored[i]);
            if (string.IsNullOrEmpty(key)) return null;
            if (restored.Count(x => id(x) == key) == 1 && local.Count(x => id(x) == key) == 1)
                return local.First(x => id(x) == key);
            var same = local.ElementAtOrDefault(i);
            return same != null && id(same) == key ? same : null;
        }

        // Stricter than WPF MainWindow.CloudBackup.cs:138-160, reused from the old port lane.
        public static void HoldSafetyFloor(AppSettings? current, AppSettings? restored)
        {
            if (current == null || restored == null) return;
            foreach (var p in typeof(AppSettings).GetProperties())
            {
                if (p.PropertyType != typeof(bool) || !p.CanRead || !p.CanWrite) continue;
                if (p.Name.IndexOf("StrictLock", StringComparison.Ordinal) < 0) continue;
                if (!(bool)p.GetValue(current)! && (bool)p.GetValue(restored)!) p.SetValue(restored, false);
            }
            if (current.PanicKeyEnabled) restored.PanicKeyEnabled = true;
            if (!current.ScreenOcrEnabled)
            {
                restored.ScreenOcrEnabled = false;
            }
        }

        // The old lane normalizes asset-relative paths for Linux. Absolute paths stay local above.
        internal static void NormalizeRelativePaths(AppSettings restored)
        {
            static HashSet<string> Slash(HashSet<string> set) => new(set.Select(p => p.Replace('\\', '/')), set.Comparer);
            restored.ActiveAssetPaths = Slash(restored.ActiveAssetPaths);
            foreach (var preset in restored.AssetPresets)
            {
                preset.DisabledAssetPaths = Slash(preset.DisabledAssetPaths);
                preset.DisabledAssetFolders = Slash(preset.DisabledAssetFolders);
            }
        }

        /// <summary>WPF App.xaml.cs:5249-5305: startup restore keeps this install's
        /// one-shots and the higher lifetime/companion history, as well as the normal local fields.</summary>
        public static void PrepareWelcomeBackRestore(AppSettings current, AppSettings restored)
        {
            KeepLocal(current, restored);
            HoldSafetyFloor(current, restored);
            restored.Welcomed = current.Welcomed;
            restored.FirstRunAssetsPromptShown = current.FirstRunAssetsPromptShown;
            restored.TotalConditioningMinutes = Math.Max(current.TotalConditioningMinutes, restored.TotalConditioningMinutes);
            foreach (var (id, progress) in current.CompanionProgressData)
                if (!restored.CompanionProgressData.TryGetValue(id, out var previous)
                    || progress.Level > previous.Level
                    || (progress.Level == previous.Level && progress.TotalXPEarned > previous.TotalXPEarned))
                    restored.CompanionProgressData[id] = progress;
        }

        // ---- the server (WPF BackupSettingsAsync / GetSettingsBackupInfoAsync / RestoreSettingsFromCloudAsync) ----

        /// <summary>The "Backup now" upload (WPF BackupSettingsAsync with force: true). False when there is
        /// no account, no token, offline, over budget, or the server refused.</summary>
        public async Task<bool> BackupAsync(AppSettings settings) => (await BackupWithStatusAsync(settings)).Ok;

        /// <summary><see cref="BackupAsync"/> plus the refusing status (413 = too large: the automatic backup
        /// stops for the session, WPF ProfileSyncService.cs:4582-4588); null status when nothing was sent.</summary>
        internal async Task<(bool Ok, HttpStatusCode? Refused)> BackupWithStatusAsync(AppSettings settings)
        {
            if (settings.OfflineMode) return (false, null);
            var unifiedId = settings.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return (false, null);
            if (string.IsNullOrEmpty(settings.AuthToken))
            {
                Log.Debug("Settings backup skipped (no auth token)");
                return (false, null);
            }
            try
            {
                var obj = StripForUpload(settings);
                // WPF 7.2.3 (#1488): a session or ramp still running keeps the user's own values in its
                // journal; the backup carries those, never the session's Hydra / ducking / pink.
                try { SessionSettingsJournal.OverlayOnBackup(obj, CorePaths.UserData); }
                catch (Exception ex) { Log.Debug("Settings backup: session journal overlay failed: {E}", ex.Message); }
                // The slot is shared with Windows, and frozen WPF restores the whole blob: a path field missing from
                // it comes back EMPTY there. So the slot's own path fields ride along unchanged (review R3).
                var slot = await FetchSlotAsync(settings);
                if (!slot.Ok)
                {
                    Log.Warning("Settings backup skipped: the current backup could not be read, so its file paths could not be kept");
                    return (false, null);
                }
                // #4323: a slot that exists but cannot be read keeps its paths only if we do not overwrite it.
                var slotSettings = SlotSettings(slot.Backup);
                if (slot.Backup != null && slotSettings == null) return (false, null);
                CarryPathFields(obj, slotSettings);
                var encoded = SettingsBackupBudget.Encode(obj);
                if (!encoded.Fits)
                {
                    Log.Warning("Settings backup skipped: {Size} bytes encoded is over the {Max} budget even after trimming [{Trimmed}]. Largest: {Largest}",
                        encoded.Base64.Length, SettingsBackupBudget.MaxEncodedBytes, string.Join(",", encoded.Trimmed),
                        string.Join(", ", SettingsBackupBudget.LargestProperties(obj).Select(x => $"{x.Name}={x.Bytes}")));
                    return (false, null);
                }
                if (encoded.Trimmed.Count > 0)
                    Log.Information("Settings backup trimmed to fit: left out {Trimmed}", string.Join(",", encoded.Trimmed));

                var body = new { unified_id = unifiedId, settings_data = encoded.Base64, app_version = CoreReleaseContent.AppVersion };
                using var response = await PostAsync("/v2/user/backup-settings", settings, body);
                if (!await SucceededAsync(response, "Settings backup")) return (false, response.StatusCode);
                Log.Information("Settings backed up to cloud ({Size} bytes compressed)", encoded.Compressed.Length);
                return (true, null);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Settings backup failed");
                return (false, null);
            }
        }

        /// <summary>The backup's date and version for the status line; null when there is none or the check failed.</summary>
        public async Task<(DateTime? BackedUpAt, string? AppVersion)?> GetInfoAsync(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.UnifiedId)) return null;
            try
            {
                var backup = await FetchAsync(settings);
                if (backup == null) return null;
                return (DateTime.TryParse((string?)backup["backed_up_at"], out var dt) ? dt : null, (string?)backup["app_version"]);
            }
            catch (Exception ex)
            {
                Log.Debug("Settings backup info check failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>The backup as settings, with <see cref="KeepLocal"/> applied against <paramref name="current"/>;
        /// null when there is none or the download failed. The caller swaps it in.</summary>
        public async Task<AppSettings?> DownloadAsync(AppSettings current)
        {
            if (string.IsNullOrEmpty(current.UnifiedId)) return null;
            try
            {
                var data = (string?)(await FetchAsync(current))?["settings_data"];
                if (data == null) return null;
                var obj = JObject.Parse(Unpack(data));
                foreach (var key in obj.Properties().Select(p => p.Name).ToList())
                    if (IsExcludedFromBackup(PropertyNameOfJsonKey(key))) obj.Remove(key);
                var restored = JsonConvert.DeserializeObject<AppSettings>(obj.ToString(),
                    new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                if (restored == null) return null;
                // Normalize downloaded presets before KeepLocal can share this machine's preset objects.
                NormalizeRelativePaths(restored);
                KeepLocal(current, restored, obj);
                HoldSafetyFloor(current, restored);
                Log.Information("Settings restored from cloud");
                return restored;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Settings restore from cloud failed");
                return null;
            }
        }

        /// <summary>The slot's <see cref="AbsolutePathFields"/> keys, copied into <paramref name="upload"/> as they are
        /// (WPF's Windows paths, or nothing when the last backup came from this head). This head's rooted paths never go up.</summary>
        internal static void CarryPathFields(JObject upload, JObject? slotSettings)
        {
            if (slotSettings == null) return;
            foreach (var p in slotSettings.Properties())
                if (AbsolutePathFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                    || AbsolutePathFields.Contains(PropertyNameOfJsonKey(p.Name), StringComparer.OrdinalIgnoreCase))
                    upload[p.Name] = p.Value.DeepClone();
            // Keep portable fields and relative paths; use slot paths for rooted/omitted item paths.
            // Reuse the restore matching rules, including preset/trigger ids and edited action ids.
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            var listNames = new[] { nameof(AppSettings.AudioLayers), nameof(AppSettings.CornerGifOverlays),
                nameof(AppSettings.KeywordTriggers), nameof(AppSettings.KeywordTriggerPresets) };
            JObject ListsOnly(JObject source) => new(source.Properties()
                .Where(p => listNames.Contains(PropertyNameOfJsonKey(p.Name), StringComparer.OrdinalIgnoreCase))
                .Select(p => new JProperty(p.Name, p.Value.DeepClone())));
            var slotLists = ListsOnly(slotSettings);
            // Only Windows-rooted slot paths may fill omitted paths; old relative values must not undo local choices.
            foreach (var p in slotLists.Descendants().OfType<JProperty>().Where(p => p.Value.Type == JTokenType.String
                && !(IsRootedPath((string?)p.Value) && ((string?)p.Value)?.StartsWith('/') == false)
                && (p.Name.ToLowerInvariant() is "path" or "gifpath" or "audiofilepath" or "filepath")).ToList())
                p.Remove();
            var local = slotLists.ToObject<AppSettings>(serializer)!;
            var portable = ListsOnly(upload).ToObject<AppSettings>(serializer)!;
            KeepListPaths(local, portable, upload);
            var merged = JObject.FromObject(portable);
            foreach (var name in listNames)
            {
                var key = upload.Properties().First(p => PropertyNameOfJsonKey(p.Name).Equals(name, StringComparison.OrdinalIgnoreCase)).Name;
                var list = merged.GetValue(name, StringComparison.OrdinalIgnoreCase)!.DeepClone();
                PreserveOmittedPaths(upload[key], list);
                upload[key] = list;
            }
            foreach (var item in Items(upload, nameof(AppSettings.AudioLayers)))
                Drop(item, nameof(AudioLayerTrack.DisplayName));
        }

        // Model defaults must not turn stripped paths into explicit empty choices on a later restore.
        // Keep a carried slot path, but omit generated blanks unless the upload explicitly carried one.
        private static void PreserveOmittedPaths(JToken? source, JToken target)
        {
            if (target is JObject obj)
                foreach (var property in obj.Properties().ToList())
                {
                    var original = (source as JObject)?.GetValue(property.Name, StringComparison.OrdinalIgnoreCase);
                    if (original == null && property.Value.Type == JTokenType.String && (string?)property.Value == ""
                        && (property.Name.ToLowerInvariant() is "path" or "gifpath" or "audiofilepath" or "filepath"))
                        property.Remove();
                    else PreserveOmittedPaths(original, property.Value);
                }
            else if (target is JArray array)
                for (var i = 0; i < array.Count; i++)
                    PreserveOmittedPaths((source as JArray)?.ElementAtOrDefault(i), array[i]);
        }

        /// <summary>The slot's settings JSON; null when there is none or it cannot be read (nothing to carry then).</summary>
        private static JObject? SlotSettings(JObject? backup)
        {
            if ((string?)backup?["settings_data"] is not { } data) return null;
            try { return JObject.Parse(Unpack(data)); }
            catch (Exception ex)
            {
                Log.Warning("Settings backup: the current backup is unreadable ({Error}); no file paths to keep", ex.Message);
                return null;
            }
        }

        private static string Unpack(string base64)
        {
            using var input = new MemoryStream(Convert.FromBase64String(base64));
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private async Task<JObject?> FetchAsync(AppSettings settings) => (await FetchSlotAsync(settings)).Backup;

        /// <summary>Ok = the server answered (Backup null: there is no backup yet); not Ok = refused or failed.</summary>
        private async Task<(bool Ok, JObject? Backup)> FetchSlotAsync(AppSettings settings)
        {
            using var response = await PostAsync("/v2/user/settings-backup", settings, new { unified_id = settings.UnifiedId });
            if (!await SucceededAsync(response, "Settings backup fetch")) return (false, null);
            // Dates stay strings (WPF read backed_up_at as a string and parsed it itself).
            var root = JsonConvert.DeserializeObject<JObject>(await response.Content.ReadAsStringAsync(),
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
            // #4323: only an explicit null is "no backup yet"; a missing or odd slot is a failed read, never an empty one.
            var backup = root?["backup"];
            if (backup == null || (backup.Type != JTokenType.Null && backup.Type != JTokenType.Object))
                return (false, null);
            return (true, backup as JObject);
        }

        private Task<HttpResponseMessage> PostAsync(string path, AppSettings settings, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ProxyBaseUrl + path)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(settings.AuthToken)) request.Headers.Add("X-Auth-Token", settings.AuthToken);
            return _http.SendAsync(request);
        }

        /// <summary>409 merged goes to contract D when the head seeded it (<see cref="V2AuthService.MergedRecovery"/>);
        /// WPF ProfileSyncService.cs:4623/4668/4715 recovers a 401 for the next attempt, without retrying
        /// a backup or restore. Shared AuthRecovery owns cooldowns, provider validation and heartbeat.</summary>
        private static async Task<bool> SucceededAsync(HttpResponseMessage response, string what)
        {
            if (response.IsSuccessStatusCode) return true;
            var error = await response.Content.ReadAsStringAsync();
            if (V2AuthService.MergedRecovery is { } merged && await merged(response, error)) return false;
            await AuthRecovery.HandleUnauthorizedAsync(response);
            Log.Warning("{What} failed: {Status} (body {Bytes} bytes)", what, (int)response.StatusCode, error?.Length ?? 0);
            return false;
        }
    }
}
