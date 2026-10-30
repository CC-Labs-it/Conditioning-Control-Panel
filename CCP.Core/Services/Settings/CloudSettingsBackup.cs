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
    /// are copies of WPF's, unchanged; WPF is frozen and keeps its own (P118), so this class has a new
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
            nameof(AppSettings.InviteGrantUntil),
            nameof(AppSettings.LastPatreonVerification),
            nameof(AppSettings.AuthToken),
            nameof(AppSettings.CustomAssetsPath),
            nameof(AppSettings.DiscordWebhookUrl),
            nameof(AppSettings.LastSeenUtc), // Local-only greeting timestamp — must never leave the device.
            // Documented local only: a restore on another PC must not switch presence sharing on.
            nameof(AppSettings.FriendsPresenceShared),
            nameof(AppSettings.ModPersonalityPreset),
            // Conversion rows (C9): machine-local stamps and the last server tier reading.
            // A restore must not bring back a stale reading or re-open a once-ever row.
            nameof(AppSettings.ServerTierSeen),
            nameof(AppSettings.InboxStamps),
            nameof(AppSettings.LastKnownEffectiveTier),
            // Free member fields mirror the server profile; the next profile read refills them.
            nameof(AppSettings.PatreonMember),
            nameof(AppSettings.PostGrants),
            nameof(AppSettings.ServerPaidUntil),
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

        /// <summary>True when a backup must not carry this property.</summary>
        internal static bool IsExcludedFromBackup(string name) =>
            ExcludedBackupProperties.Contains(name)
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
            // Conversion state lives on this PC only (it is left out of the backup): a restore must
            // not wipe it, or the once-ever rows post again and a post-code grant goes missing.
            restored.InboxStamps = current.InboxStamps;
            restored.ServerTierSeen = current.ServerTierSeen;
            restored.PatreonMember = current.PatreonMember;
            restored.PostGrants = current.PostGrants;
            restored.ServerPaidUntil = current.ServerPaidUntil;
            restored.ModPersonalityPreset = current.ModPersonalityPreset; // the setter copies
            restored.ScrolllerOfficialNoteSeen = current.ScrolllerOfficialNoteSeen;
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

        /// <summary>Settings that hold an absolute path, a list of them, or list items carrying one
        /// (AudioLayerTrack.Path, CornerGifOverlaySetting.GifPath, PlayAudioAction.FilePath). This machine's values are
        /// never uploaded: the upload carries the SLOT's values unchanged (<see cref="CarryPathFields"/>), so a Windows
        /// restore of a backup made here keeps its own paths. A restore keeps this machine's values, whatever the backup
        /// carries (frozen WPF still uploads its Windows paths). Whole lists, so a restore never drops this machine's file from an item.
        /// The asset lists are NOT here: they are relative and forward-slashed, so they travel.</summary>
        public static readonly IReadOnlyList<string> AbsolutePathFields = new[]
        {
            nameof(AppSettings.CustomAssetsPath), nameof(AppSettings.StartupVideoPath), nameof(AppSettings.SpiralPath),
            nameof(AppSettings.MindWipeAudioPath), nameof(AppSettings.WallpaperOriginalPath), nameof(AppSettings.WallpaperSourceFolder),
            nameof(AppSettings.BlinkTrainerFolders), nameof(AppSettings.DeeperRecentFiles), nameof(AppSettings.DeeperLastDirectory),
            nameof(AppSettings.AudioLayers), nameof(AppSettings.CornerGifOverlays), nameof(AppSettings.KeywordTriggers),
        };

        private static readonly System.Reflection.PropertyInfo[] PathProperties =
            AbsolutePathFields.Select(n => typeof(AppSettings).GetProperty(n)!).ToArray();

        /// <summary>The upload JSON: the settings minus WPF's exclusions minus <see cref="AbsolutePathFields"/>.</summary>
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
            return obj;
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
        /// <see cref="AbsolutePathFields"/> value.
        /// </summary>
        internal static void KeepLocal(AppSettings current, AppSettings restored)
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
        }

        // ---- the server (WPF BackupSettingsAsync / GetSettingsBackupInfoAsync / RestoreSettingsFromCloudAsync) ----

        /// <summary>The "Backup now" upload (WPF BackupSettingsAsync with force: true). False when there is
        /// no account, no token, offline, over budget, or the server refused.</summary>
        public async Task<bool> BackupAsync(AppSettings settings)
        {
            if (settings.OfflineMode) return false;
            var unifiedId = settings.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return false;
            if (string.IsNullOrEmpty(settings.AuthToken))
            {
                Log.Debug("Settings backup skipped (no auth token)");
                return false;
            }
            try
            {
                var obj = StripForUpload(settings);
                // The slot is shared with Windows, and frozen WPF restores the whole blob: a path field missing from
                // it comes back EMPTY there. So the slot's own path fields ride along unchanged (review R3).
                var slot = await FetchSlotAsync(settings);
                if (!slot.Ok)
                {
                    Log.Warning("Settings backup skipped: the current backup could not be read, so its file paths could not be kept");
                    return false;
                }
                CarryPathFields(obj, SlotSettings(slot.Backup));
                var encoded = SettingsBackupBudget.Encode(obj);
                if (!encoded.Fits)
                {
                    Log.Warning("Settings backup skipped: {Size} bytes encoded is over the {Max} budget even after trimming [{Trimmed}]. Largest: {Largest}",
                        encoded.Base64.Length, SettingsBackupBudget.MaxEncodedBytes, string.Join(",", encoded.Trimmed),
                        string.Join(", ", SettingsBackupBudget.LargestProperties(obj).Select(x => $"{x.Name}={x.Bytes}")));
                    return false;
                }
                if (encoded.Trimmed.Count > 0)
                    Log.Information("Settings backup trimmed to fit: left out {Trimmed}", string.Join(",", encoded.Trimmed));

                var body = new { unified_id = unifiedId, settings_data = encoded.Base64, app_version = CoreReleaseContent.AppVersion };
                using var response = await PostAsync("/v2/user/backup-settings", settings, body);
                if (!await SucceededAsync(response, "Settings backup")) return false;
                Log.Information("Settings backed up to cloud ({Size} bytes compressed)", encoded.Compressed.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Settings backup failed");
                return false;
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
                var json = Unpack(data);
                var restored = JsonConvert.DeserializeObject<AppSettings>(json,
                    new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                if (restored == null) return null;
                KeepLocal(current, restored);
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
        /// (WPF's Windows paths, or nothing when the last backup came from this head). This head's own paths never go up.</summary>
        internal static void CarryPathFields(JObject upload, JObject? slotSettings)
        {
            if (slotSettings == null) return;
            foreach (var p in slotSettings.Properties())
                if (AbsolutePathFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                    || AbsolutePathFields.Contains(PropertyNameOfJsonKey(p.Name), StringComparer.OrdinalIgnoreCase))
                    upload[p.Name] = p.Value.DeepClone();
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
            return (true, root?["backup"] as JObject);
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
        /// everything else non-2xx is a logged failure. ponytail: WPF's 401 token recovery has no twin on this
        /// head yet (SyncPush has none either), so a 401 is a plain failure here.</summary>
        private static async Task<bool> SucceededAsync(HttpResponseMessage response, string what)
        {
            if (response.IsSuccessStatusCode) return true;
            var error = await response.Content.ReadAsStringAsync();
            if (V2AuthService.MergedRecovery is { } merged && await merged(response, error)) return false;
            Log.Warning("{What} failed: {Status} (body {Bytes} bytes)", what, (int)response.StatusCode, error?.Length ?? 0);
            return false;
        }
    }
}
