using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public sealed class CloudBackupInfo
    {
        public string? AppVersion { get; set; }
        public DateTime? BackedUpAt { get; set; }
        public int SizeBytes { get; set; }
    }

    public sealed class CloudSettingsBackup
    {
        public const string ServerUrl = "https://codebambi-proxy.vercel.app";

        public const int MaxEncodedBytes = 660_000;

        public static readonly IReadOnlyList<string> TrimOrder = new[]
        {
            "ActiveAssetPaths", "DisabledAssetPaths", "AssetPresets",
        };

        private static readonly string[] ExcludedPropertyNames =
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
            nameof(AppSettings.LastSeenUtc),
            nameof(AppSettings.FriendsPresenceShared),
            nameof(AppSettings.ModPersonalityPreset),
            nameof(AppSettings.ServerTierSeen), nameof(AppSettings.InboxStamps),
            nameof(AppSettings.LastKnownEffectiveTier), nameof(AppSettings.PatreonMember),
            nameof(AppSettings.PostGrants), nameof(AppSettings.ServerPaidUntil),
            nameof(AppSettings.ScrolllerOfficialNoteSeen),
        };

        private static readonly string[] SecretMarkers = { "Token", "ApiKey", "Secret", "Password", "Webhook", "Credential" };

        private static readonly PropertyInfo[] SettingsProperties =
            typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        internal static readonly PropertyInfo[] ChasterLocalProperties = SettingsProperties
            .Where(p => p.Name.StartsWith("Chaster", StringComparison.Ordinal)
                        && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                        && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)))
            .ToArray();

        private static readonly HashSet<string> ExcludedKeys = BuildExcludedKeys();

        private static HashSet<string> BuildExcludedKeys()
        {
            var keys = new HashSet<string>(ExcludedPropertyNames, StringComparer.OrdinalIgnoreCase);
            foreach (var p in SettingsProperties)
            {
                var json = p.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
                if (string.IsNullOrEmpty(json)) continue;
                if (keys.Contains(p.Name) || IsChasterName(p.Name) || IsSecretName(p.Name) || IsAudioName(p.Name)) keys.Add(json!);
            }
            return keys;
        }

        private static bool IsAudioName(string name) => name.StartsWith("AudioPlaylist", StringComparison.OrdinalIgnoreCase);

        private static bool IsChasterName(string name) => name.StartsWith("Chaster", StringComparison.OrdinalIgnoreCase);

        private static bool IsSecretName(string name) =>
            SecretMarkers.Any(m => name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

        public static bool IsExcludedFromBackup(string? name) =>
            !string.IsNullOrEmpty(name) && (ExcludedKeys.Contains(name!) || IsChasterName(name!) || IsSecretName(name!) || IsAudioName(name!));

        public static JObject BuildBackupObject(AppSettings settings)
        {
            var obj = JObject.Parse(JsonConvert.SerializeObject(settings, Formatting.None));
            foreach (var key in obj.Properties().Select(p => p.Name).ToList())
                if (IsExcludedFromBackup(key)) obj.Remove(key);
            return obj;
        }


        public sealed class Encoded
        {
            public byte[] Compressed { get; init; } = Array.Empty<byte>();
            public string Base64 { get; init; } = "";
            public List<string> Trimmed { get; init; } = new();
            public int Budget { get; init; } = MaxEncodedBytes;
            public bool Fits => Base64.Length <= Budget;
        }

        public static Encoded Encode(JObject settings, int budget = MaxEncodedBytes)
        {
            var trimmed = new List<string>();
            var removed = new List<JProperty>();
            var (gz, b64) = Pack(settings);
            foreach (var key in TrimOrder)
            {
                if (b64.Length <= budget) break;
                var prop = settings.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                if (prop == null) continue;
                prop.Remove();
                removed.Add(prop);
                trimmed.Add(prop.Name);
                (gz, b64) = Pack(settings);
            }
            for (int i = removed.Count - 2; i >= 0 && b64.Length <= budget; i--)
            {
                settings.Add(removed[i]);
                var (g, b) = Pack(settings);
                if (b.Length <= budget) { (gz, b64) = (g, b); trimmed.Remove(removed[i].Name); }
                else removed[i].Remove();
            }
            return new Encoded { Compressed = gz, Base64 = b64, Trimmed = trimmed, Budget = budget };
        }

        private static (byte[] Gz, string B64) Pack(JObject settings)
        {
            var bytes = Encoding.UTF8.GetBytes(settings.ToString(Formatting.None));
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(bytes, 0, bytes.Length);
            var gz = output.ToArray();
            return (gz, Convert.ToBase64String(gz));
        }

        public static AppSettings? Decode(string base64)
        {
            string json;
            using (var input = new MemoryStream(Convert.FromBase64String(base64)))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
                json = reader.ReadToEnd();
            var obj = JObject.Parse(json);
            foreach (var key in obj.Properties().Select(p => p.Name).ToList())
                if (IsExcludedFromBackup(key)) obj.Remove(key);
            return obj.ToObject<AppSettings>(JsonSerializer.Create(new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
            }));
        }


        public static void PreserveLocalOnlyFields(AppSettings? current, AppSettings? restored)
        {
            if (current == null || restored == null) return;
            restored.CustomAssetsPath = current.CustomAssetsPath;
            restored.DiscordWebhookUrl = current.DiscordWebhookUrl;
            restored.LastSeenUtc = current.LastSeenUtc;
            restored.FriendsPresenceShared = current.FriendsPresenceShared;
            restored.ModPersonalityPreset = current.ModPersonalityPreset; // the setter copies
            if (restored.DisabledAssetPaths.Count == 0 && current.DisabledAssetPaths.Count > 0)
                restored.DisabledAssetPaths = new HashSet<string>(current.DisabledAssetPaths);
            if (restored.ActiveAssetPaths.Count == 0 && current.ActiveAssetPaths.Count > 0)
                restored.ActiveAssetPaths = new HashSet<string>(current.ActiveAssetPaths);
            if (restored.AssetPresets.Count == 0 && current.AssetPresets.Count > 0)
                restored.AssetPresets = new List<AssetPreset>(current.AssetPresets);
            restored.InboxStamps = current.InboxStamps;
            restored.ServerTierSeen = current.ServerTierSeen;
            restored.LastKnownEffectiveTier = current.LastKnownEffectiveTier;
            restored.PatreonMember = current.PatreonMember;
            restored.PostGrants = current.PostGrants;
            restored.ServerPaidUntil = current.ServerPaidUntil;
            restored.ScrolllerOfficialNoteSeen = current.ScrolllerOfficialNoteSeen;
            foreach (var p in ChasterLocalProperties.Concat(SettingsProperties.Where(p =>
                         IsAudioName(p.Name) && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                         && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)))))
            {
                var v = p.GetValue(current);
                if (v is List<string> list) v = new List<string>(list);
                p.SetValue(restored, v);
            }
        }

        public static void HoldSafetyFloor(AppSettings? current, AppSettings? restored)
        {
            if (current == null || restored == null) return;
            foreach (var p in SettingsProperties)
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

        public static void ApplyLocalWins(AppSettings? current, AppSettings? restored)
        {
            if (current == null || restored == null) return;
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
            PreserveLocalOnlyFields(current, restored);
            HoldSafetyFloor(current, restored);
        }

        // Absolute paths KeepLocalMissingPaths guards; "X.Y" = property Y of each entry of list X.
        internal static readonly string[] GuardedPathFields =
        {
            nameof(AppSettings.StartupVideoPath), nameof(AppSettings.SpiralPath), nameof(AppSettings.MindWipeAudioPath),
            nameof(AppSettings.WallpaperSourceFolder), nameof(AppSettings.WallpaperOriginalPath), nameof(AppSettings.DeeperLastDirectory),
            nameof(AppSettings.BlinkTrainerFolders), nameof(AppSettings.DeeperRecentFiles),
            "CornerGifOverlays.GifPath", "AudioLayers.Path", "KeywordTriggers.AudioFilePath",
        };

        // Relative to the assets folder: passed through with '\' turned into '/'.
        internal static readonly string[] RelativePathFields =
        {
            nameof(AppSettings.ActiveAssetPaths), nameof(AppSettings.DisabledAssetPaths), nameof(AppSettings.DisabledAssetFolders),
            "AssetPresets.DisabledAssetPaths", "AssetPresets.DisabledAssetFolders",
        };

        /// <summary>
        /// Avalonia manual restore only (decision 2026-10-25): a backup from another machine (or OS) carries
        /// absolute paths that do not exist here, so each such field keeps the local value instead. WPF never
        /// calls this. Returns the fields that kept their local value, for the "restored" dialog.
        /// </summary>
        public static List<string> KeepLocalMissingPaths(AppSettings? current, AppSettings? restored)
        {
            var kept = new List<string>();
            if (current == null || restored == null) return kept;
            static bool Here(string? p) => string.IsNullOrEmpty(p) || File.Exists(p) || Directory.Exists(p);
            string? One(string name, string? local, string? theirs)
            {
                if (Here(theirs) || theirs == local) return theirs;
                kept.Add(name);
                return local;
            }
            List<T> Many<T>(string name, List<T> local, List<T> theirs, Func<T, string?> path)
            {
                var keep = theirs.Where(t => Here(path(t))).ToList();
                if (keep.Count < theirs.Count) kept.Add(name);
                keep.AddRange(local.Where(l => !keep.Any(k => path(k) == path(l))));   // never drop a local entry
                return keep;
            }

            restored.StartupVideoPath = One(nameof(AppSettings.StartupVideoPath), current.StartupVideoPath, restored.StartupVideoPath);
            restored.SpiralPath = One(nameof(AppSettings.SpiralPath), current.SpiralPath, restored.SpiralPath)!;
            restored.MindWipeAudioPath = One(nameof(AppSettings.MindWipeAudioPath), current.MindWipeAudioPath, restored.MindWipeAudioPath)!;
            restored.WallpaperSourceFolder = One(nameof(AppSettings.WallpaperSourceFolder), current.WallpaperSourceFolder, restored.WallpaperSourceFolder)!;
            restored.WallpaperOriginalPath = One(nameof(AppSettings.WallpaperOriginalPath), current.WallpaperOriginalPath, restored.WallpaperOriginalPath)!;
            restored.DeeperLastDirectory = One(nameof(AppSettings.DeeperLastDirectory), current.DeeperLastDirectory, restored.DeeperLastDirectory)!;
            restored.BlinkTrainerFolders = Many(nameof(AppSettings.BlinkTrainerFolders), current.BlinkTrainerFolders, restored.BlinkTrainerFolders, p => p);
            restored.DeeperRecentFiles = Many(nameof(AppSettings.DeeperRecentFiles), current.DeeperRecentFiles, restored.DeeperRecentFiles, p => p);
            restored.AudioLayers = Many(nameof(AppSettings.AudioLayers), current.AudioLayers, restored.AudioLayers, l => l.Path);
            for (var i = 0; i < restored.CornerGifOverlays.Count; i++)   // positional slots: slot i keeps local slot i
                restored.CornerGifOverlays[i].GifPath = One(nameof(AppSettings.CornerGifOverlays),
                    i < current.CornerGifOverlays.Count ? current.CornerGifOverlays[i].GifPath : "", restored.CornerGifOverlays[i].GifPath) ?? "";
            foreach (var t in restored.KeywordTriggers)   // by Id: a trigger new to this machine gets no file
                t.AudioFilePath = One(nameof(AppSettings.KeywordTriggers),
                    current.KeywordTriggers.FirstOrDefault(l => l.Id == t.Id)?.AudioFilePath, t.AudioFilePath);

            static HashSet<string> Slash(HashSet<string> set) => new(set.Select(p => p.Replace('\\', '/')), set.Comparer);
            restored.ActiveAssetPaths = Slash(restored.ActiveAssetPaths);
            restored.DisabledAssetPaths = Slash(restored.DisabledAssetPaths);
            restored.DisabledAssetFolders = Slash(restored.DisabledAssetFolders);
            foreach (var preset in restored.AssetPresets)
            {
                preset.DisabledAssetPaths = Slash(preset.DisabledAssetPaths);
                preset.DisabledAssetFolders = Slash(preset.DisabledAssetFolders);
            }
            return kept.Distinct().ToList();
        }


        private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        private readonly HttpClient _http;
        private readonly Func<AppSettings?> _settings;
        private readonly Func<string?> _token;

        public CloudSettingsBackup(Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null, Func<string?>? token = null)
        {
            _settings = settings ?? (() => CoreSettings.Current);
            _token = token ?? (() => _settings()?.AuthToken);
            _http = handler == null ? Shared.Value : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public bool HasIdentity
        {
            get
            {
                var s = _settings();
                return s != null && !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(_token());
            }
        }

        private HttpRequestMessage NewRequest(string path, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ServerUrl + path);
            request.Headers.Add("X-Auth-Token", _token());
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            return request;
        }

        public async Task<bool> BackupAsync()
        {
            var s = _settings();
            if (s == null || s.OfflineMode || !HasIdentity) return false;
            try
            {
                var obj = BuildBackupObject(s);
                var encoded = Encode(obj);
                if (!encoded.Fits)
                {
                    Log.Warning("Settings backup skipped: {Size} bytes encoded is over the {Max} budget even after trimming [{Trimmed}]",
                        encoded.Base64.Length, MaxEncodedBytes, string.Join(",", encoded.Trimmed));
                    return false;
                }
                using var request = NewRequest("/v2/user/backup-settings", new
                {
                    unified_id = s.UnifiedId,
                    settings_data = encoded.Base64,
                    app_version = CoreReleaseContent.AppVersion,
                });
                using var response = await _http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false)) return false;
                    Log.Warning("Settings backup failed: {Status}", response.StatusCode);
                    return false;
                }
                Log.Information("Settings backed up to cloud ({Size} bytes compressed)", encoded.Compressed.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("Settings backup did not finish: {ExType}: {Error}", ex.GetType().Name, ex.Message);
                return false;
            }
        }

        private async Task<JToken?> FetchAsync()
        {
            var s = _settings();
            if (s == null || !HasIdentity) return null;
            using var request = NewRequest("/v2/user/settings-backup", new { unified_id = s.UnifiedId });
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var backup = JObject.Parse(json)["backup"];
            return backup == null || backup.Type != JTokenType.Object ? null : backup;
        }

        public async Task<CloudBackupInfo?> GetInfoAsync()
        {
            var backup = await FetchAsync().ConfigureAwait(false);
            if (backup == null) return null;
            return new CloudBackupInfo
            {
                AppVersion = (string?)backup["app_version"],
                BackedUpAt = backup["backed_up_at"] is { Type: JTokenType.Date } d ? (DateTime)d
                    : DateTime.TryParse((string?)backup["backed_up_at"], System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null,
                SizeBytes = (int?)backup["size_bytes"] ?? 0,
            };
        }

        public async Task<AppSettings?> DownloadAsync()
        {
            try
            {
                var backup = await FetchAsync().ConfigureAwait(false);
                var data = (string?)backup?["settings_data"];
                if (string.IsNullOrEmpty(data)) return null;
                var restored = Decode(data!);
                Log.Information("Settings downloaded from cloud (v{Version})", (string?)backup!["app_version"]);
                return restored;
            }
            catch (Exception ex)
            {
                Log.Error("Settings restore from cloud failed: {ExType}: {Error}", ex.GetType().Name, ex.Message);
                return null;
            }
        }
    }
}
