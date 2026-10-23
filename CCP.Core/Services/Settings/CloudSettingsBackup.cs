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
    /// <para>On top of WPF (docs/avalonia-decisions.md, cloud settings backup): <see cref="AbsolutePathFields"/>
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
            || (name != null && name.StartsWith("Chaster", StringComparison.OrdinalIgnoreCase));

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
            restored.ModPersonalityPreset = current.ModPersonalityPreset; // the setter copies
            // A backup over budget leaves out the per-file asset lists (SettingsBackupBudget), so
            // an empty list in the restore may just mean "not carried": keep this PC's own.
            if (restored.DisabledAssetPaths.Count == 0 && current.DisabledAssetPaths.Count > 0)
                restored.DisabledAssetPaths = new HashSet<string>(current.DisabledAssetPaths);
            if (restored.ActiveAssetPaths.Count == 0 && current.ActiveAssetPaths.Count > 0)
                restored.ActiveAssetPaths = new HashSet<string>(current.ActiveAssetPaths);
            if (restored.AssetPresets.Count == 0 && current.AssetPresets.Count > 0)
                restored.AssetPresets = new List<AssetPreset>(current.AssetPresets);
            foreach (var p in ChasterLocalProperties)
            {
                var v = p.GetValue(current);
                if (v is List<string> list) v = new List<string>(list);
                p.SetValue(restored, v);
            }
        }

        // ---- new: absolute paths are machine-local ----

        /// <summary>Whole settings that hold machine-local absolute paths. Item paths in the
        /// three portable lists are handled separately. Relative asset lists travel unchanged.</summary>
        public static readonly IReadOnlyList<string> AbsolutePathFields = new[]
        {
            nameof(AppSettings.CustomAssetsPath), nameof(AppSettings.StartupVideoPath), nameof(AppSettings.SpiralPath),
            nameof(AppSettings.MindWipeAudioPath), nameof(AppSettings.WallpaperOriginalPath), nameof(AppSettings.WallpaperSourceFolder),
            nameof(AppSettings.BlinkTrainerFolders), nameof(AppSettings.DeeperRecentFiles), nameof(AppSettings.DeeperLastDirectory),
        };

        private static readonly System.Reflection.PropertyInfo[] PathProperties =
            AbsolutePathFields.Select(n => typeof(AppSettings).GetProperty(n)!).ToArray();

        /// <summary>The upload JSON: settings without WPF exclusions, whole path settings or list-item paths.</summary>
        internal static JObject StripForUpload(AppSettings settings)
        {
            var obj = JObject.Parse(JsonConvert.SerializeObject(settings, Formatting.None));
            foreach (var key in obj.Properties().Select(p => p.Name).ToList())
            {
                // JSON names may differ in case from the C# names; match case-insensitively.
                if (IsExcludedFromBackup(key) || AbsolutePathFields.Contains(key, StringComparer.OrdinalIgnoreCase))
                    obj.Remove(key);
            }
            foreach (var item in Items(obj, nameof(AppSettings.AudioLayers)))
            {
                Drop(item, nameof(AudioLayerTrack.Path));
                Drop(item, nameof(AudioLayerTrack.DisplayName)); // computed from Path, not persisted meaningfully
            }
            foreach (var item in Items(obj, nameof(AppSettings.CornerGifOverlays)))
                Drop(item, nameof(CornerGifOverlaySetting.GifPath));
            var triggers = Items(obj, nameof(AppSettings.KeywordTriggers))
                .Concat(Items(obj, nameof(AppSettings.KeywordTriggerPresets)).SelectMany(p => Items(p, "triggers")));
            foreach (var item in triggers)
            {
                Drop(item, nameof(KeywordTrigger.AudioFilePath)); // legacy dispatch fallback
                foreach (var action in Items(item, nameof(KeywordTrigger.Actions)))
                    if (string.Equals((string?)action.GetValue("type", StringComparison.OrdinalIgnoreCase), "PlayAudio", StringComparison.OrdinalIgnoreCase))
                        Drop(action, "filePath");
            }
            return obj;
        }

        // Case-insensitive: a serializer naming policy must not let a path slip through.
        private static IEnumerable<JObject> Items(JObject o, string list) =>
            (o.GetValue(list, StringComparison.OrdinalIgnoreCase) as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
        private static void Drop(JObject o, string name) => o.Property(name, StringComparison.OrdinalIgnoreCase)?.Remove();

        /// <summary>
        /// What a restore keeps from this machine: WPF's manual-restore identity/progression list
        /// (MainWindow.CloudBackup.cs:138-157), <see cref="PreserveLocalOnlyFields"/>, then every
        /// <see cref="AbsolutePathFields"/> value and matched list-item paths.
        /// </summary>
        internal static void KeepLocal(AppSettings current, AppSettings restored, JObject backup)
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

            // No stable id/name on tracks or corner slots: their persisted order is the key.
            // Check JSON presence: a missing old list keeps local items; an explicit [] clears it.
            bool Missing(string name) => backup.GetValue(name, StringComparison.OrdinalIgnoreCase) == null;
            if (Missing(nameof(AppSettings.AudioLayers))) restored.AudioLayers = new(current.AudioLayers);
            else for (var i = 0; i < restored.AudioLayers.Count; i++)
                restored.AudioLayers[i].Path = current.AudioLayers.ElementAtOrDefault(i)?.Path ?? "";
            if (Missing(nameof(AppSettings.CornerGifOverlays))) restored.CornerGifOverlays = new(current.CornerGifOverlays);
            else for (var i = 0; i < restored.CornerGifOverlays.Count; i++)
                restored.CornerGifOverlays[i].GifPath = current.CornerGifOverlays.ElementAtOrDefault(i)?.GifPath ?? "";
            if (Missing(nameof(AppSettings.KeywordTriggers))) restored.KeywordTriggers = new(current.KeywordTriggers);
            else RestoreTriggerPaths(restored.KeywordTriggers, current.KeywordTriggers);
            if (Missing(nameof(AppSettings.KeywordTriggerPresets))) restored.KeywordTriggerPresets = new(current.KeywordTriggerPresets);
            else for (var i = 0; i < restored.KeywordTriggerPresets.Count; i++)
                RestoreTriggerPaths(restored.KeywordTriggerPresets[i].Triggers,
                    ById(restored.KeywordTriggerPresets, i, current.KeywordTriggerPresets, p => p.Id)?.Triggers ?? new());
        }

        private static void RestoreTriggerPaths(List<KeywordTrigger> restored, List<KeywordTrigger> current)
        {
            for (var i = 0; i < restored.Count; i++)
            {
                var trigger = restored[i];
                var local = ById(restored, i, current, t => t.Id);
                trigger.AudioFilePath = local?.AudioFilePath ?? "";
                // Action ids are not stable (every edit runs RebuildActionsFromFlatFields, which mints new
                // ones), so a miss falls back to the local PlayAudio at the same position, then AudioFilePath.
                var localAudio = local?.Actions.OfType<PlayAudioAction>().ToList() ?? new();
                var restoredAudio = trigger.Actions.OfType<PlayAudioAction>().ToList();
                for (var j = 0; j < restoredAudio.Count; j++)
                    restoredAudio[j].FilePath = local == null ? ""
                        : (ById(restoredAudio, j, localAudio, a => a.Id) ?? localAudio.ElementAtOrDefault(j))?.FilePath
                          ?? local.AudioFilePath ?? "";
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
                string json;
                using (var input = new MemoryStream(Convert.FromBase64String(data)))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                    json = await reader.ReadToEndAsync();
                var restored = JsonConvert.DeserializeObject<AppSettings>(json,
                    new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                if (restored == null) return null;
                KeepLocal(current, restored, JObject.Parse(json));
                Log.Information("Settings restored from cloud");
                return restored;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Settings restore from cloud failed");
                return null;
            }
        }

        private async Task<JObject?> FetchAsync(AppSettings settings)
        {
            using var response = await PostAsync("/v2/user/settings-backup", settings, new { unified_id = settings.UnifiedId });
            if (!await SucceededAsync(response, "Settings backup fetch")) return null;
            // Dates stay strings (WPF read backed_up_at as a string and parsed it itself).
            var root = JsonConvert.DeserializeObject<JObject>(await response.Content.ReadAsStringAsync(),
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
            return root?["backup"] as JObject;
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
