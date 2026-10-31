using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core <see cref="CloudSettingsBackup"/> on the wire (WPF ProfileSyncService.cs:4636-4890 endpoints and
/// bodies), plus the cross-OS rule: absolute paths never leave this machine and a restore keeps this machine's.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class CloudSettingsBackupTests : IDisposable
{
    private const string Url = "https://codebambi-proxy.vercel.app";
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly Fake _fake = new();

    public CloudSettingsBackupTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
    }

    public void Dispose()
    {
        V2AuthService.MergedRecovery = null;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, string? Token, string Body)> Seen = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"success\":true}";
        public Func<string, string?>? BodyFor;   // per url; null falls back to Body

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.RequestUri!.AbsoluteUri, r.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null,
                await r.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Status) { Content = new StringContent(BodyFor?.Invoke(r.RequestUri.AbsoluteUri) ?? Body) };
        }
    }

    /// <summary>The uploads only (a backup reads the slot first, to keep its path fields).</summary>
    private List<(string Url, string? Token, string Body)> Uploads =>
        _fake.Seen.Where(s => s.Url == Url + "/v2/user/backup-settings").ToList();

    private AppSettings Signed()
    {
        var s = new AppSettings { UnifiedId = "u_abc", AuthToken = "TOK" };
        s.SpiralPath = "/home/me/spiral.gif";
        s.MindWipeAudioPath = "/home/me/wipe.mp3";
        s.BlinkTrainerFolders = new List<string> { "/home/me/blink" };
        s.DeeperRecentFiles = new List<string> { "/home/me/deep.json" };
        s.CustomAssetsPath = "/home/me/assets";
        s.DisabledAssetPaths = new HashSet<string> { "images/a.png" };
        s.AssetPresets = new List<AssetPreset> { new() { Name = "p" } };
        // List settings whose items carry an absolute path (review P1/P2).
        s.AudioLayers = new List<AudioLayerTrack> { new() { Path = "/home/me/layer.mp3", Volume = 42 } };
        s.CornerGifOverlays = new List<CornerGifOverlaySetting> { new() { GifPath = "/home/me/corner.gif", Size = 222 } };
        s.KeywordTriggers = new List<KeywordTrigger> { new() { Id = "trigger", Keyword = "k", AudioFilePath = "/home/me/legacy.mp3", Actions = new List<KeywordAction> { new PlayAudioAction { Id = "audio", FilePath = "/home/me/k.mp3" }, new AddXpAction { Amount = 17 } } } };
        s.KeywordTriggerPresets = new List<KeywordTriggerPreset> { new() { Id = "preset", Triggers = new List<KeywordTrigger> { new() { Id = "pt", AudioFilePath = "/home/me/preset-legacy.mp3",
            Actions = new List<KeywordAction> { new PlayAudioAction { Id = "pa", FilePath = "/home/me/preset.mp3" } } } } } };
        return s;
    }

    private static string Pack(string json)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(output.ToArray());
    }

    private static JObject Unpack(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        return JObject.Parse(reader.ReadToEnd());
    }

    [Theory]
    [InlineData("/home/me/layer.mp3")]
    [InlineData(@"C:\Users\me\layer.mp3")]
    public async Task BackupUploadsNoAbsolutePathNoExcludedPropertyAndKeepsTheAssetLists(string layerPath)
    {
        var s = Signed();
        s.AudioLayers[0].Path = layerPath;
        s.OpenRouterApiKey = "sk-secret";
        s.FlashEnabled = false;

        Assert.True(await new CloudSettingsBackup(_fake).BackupAsync(s));

        var (url, token, body) = Assert.Single(Uploads);
        Assert.Equal(Url + "/v2/user/backup-settings", url);
        Assert.Equal("TOK", token);
        var req = JObject.Parse(body);
        Assert.Equal("u_abc", (string?)req["unified_id"]);
        Assert.NotNull(req["app_version"]);
        var sent = Unpack((string)req["settings_data"]!);
        var keys = sent.Properties().Select(p => p.Name).ToList();
        foreach (var path in CloudSettingsBackup.AbsolutePathFields) Assert.DoesNotContain(path, keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(keys, k => CloudSettingsBackup.IsExcludedFromBackup(k));
        Assert.DoesNotContain("OpenRouterApiKey", keys);
        Assert.Equal(42, (int)Assert.Single(sent["AudioLayers"]!)["Volume"]!);
        Assert.Null(sent["AudioLayers"]![0]!["Path"]);
        Assert.Equal(222, (int)Assert.Single(sent["CornerGifOverlays"]!)["Size"]!);
        Assert.Null(sent["CornerGifOverlays"]![0]!["GifPath"]);
        var trigger = Assert.Single(sent["KeywordTriggers"]!);
        Assert.Equal("k", (string?)trigger["Keyword"]);
        Assert.Null(trigger["AudioFilePath"]);
        Assert.Null(trigger["Actions"]![0]!["filePath"]);
        Assert.Equal(17, (int)trigger["Actions"]![1]!["amount"]!);
        Assert.Equal(layerPath, s.AudioLayers[0].Path); // stripping must not mutate live settings
        Assert.DoesNotContain("/home/me", sent.ToString());
        Assert.DoesNotContain(@"C:\", sent.ToString());
        Assert.False((bool)sent["FlashEnabled"]!);
        Assert.Contains("images/a.png", sent["DisabledAssetPaths"]!.Values<string>());
        Assert.Single(sent["AssetPresets"]!);
    }

    /// <summary>R2-3 (port review 7.2): the Core list said "copied unchanged from WPF" and had gone stale. Every
    /// property the WPF head keeps out of its backup is kept out here too, so a Linux backup never uploads
    /// conversion state or playlist paths, and a restore never wipes the once-ever inbox rows.</summary>
    [Fact]
    public void ExcludesEverythingTheWpfBackupExcludesAndKeepsItLocalOnRestore()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var wpf = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Settings", "ProfileSyncService.cs"));
        int start = wpf.IndexOf("HashSet<string> ExcludedBackupProperties", StringComparison.Ordinal);
        Assert.True(start >= 0, "WPF ExcludedBackupProperties not found");
        var block = wpf.Substring(start, wpf.IndexOf("};", start, StringComparison.Ordinal) - start);
        var names = System.Text.RegularExpressions.Regex.Matches(block, @"nameof\(AppSettings\.(\w+)\)").Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("InboxStamps", names);
        foreach (var name in names) Assert.True(CloudSettingsBackup.IsExcludedFromBackup(name), name + " is uploaded");
        Assert.True(CloudSettingsBackup.IsExcludedFromBackup("AudioPlaylistPaths"));
        Assert.True(CloudSettingsBackup.IsExcludedFromBackup(nameof(AppSettings.ConsentOwnedFields)));

        // The real upload, not just the predicate: several of these serialize under a [JsonProperty] name.
        var all = Signed();
        all.InboxStamps["welcome"] = "2026-10-01";
        all.PostGrants["code1"] = "g";
        all.ServerTierSeen = 2;
        all.PatreonMember = true;
        all.LastKnownEffectiveTier = 2;
        all.ServerPaidUntil = DateTime.UtcNow;
        all.ScrolllerOfficialNoteSeen = true;
        all.ConsentOwnedAccount = "u_abc";
        all.ConsentOwnedFields = 3;
        var sentKeys = CloudSettingsBackup.StripForUpload(all).Properties()
            .Select(p => CloudSettingsBackup.PropertyNameOfJsonKey(p.Name)).ToList();
        foreach (var name in names.Concat(new[] { nameof(AppSettings.ConsentOwnedAccount), nameof(AppSettings.ConsentOwnedFields), "AudioPlaylistPaths" }))
            Assert.DoesNotContain(name, sentKeys, StringComparer.OrdinalIgnoreCase);

        var current = new AppSettings { ScrolllerOfficialNoteSeen = true, ConsentOwnedFields = 3 };
        current.PostGrants["code1"] = "g";
        current.InboxStamps["welcome"] = "2026-10-01";
        var restored = new AppSettings();
        CloudSettingsBackup.PreserveLocalOnlyFields(current, restored);
        Assert.Equal("2026-10-01", restored.InboxStamps["welcome"]);
        Assert.Equal("g", restored.PostGrants["code1"]);
        Assert.True(restored.ScrolllerOfficialNoteSeen);
        Assert.Equal(3, restored.ConsentOwnedFields);
    }

    [Fact]
    public async Task AWindowsBackupRestoredHereKeepsThisMachinesPathsAndProgression()
    {
        // What frozen WPF uploads: its own absolute paths, its own level.
        var windows = new AppSettings
        {
            SpiralPath = @"C:\Users\me\spiral.gif", MindWipeAudioPath = @"C:\wipe.mp3", WallpaperSourceFolder = @"D:\walls",
            BlinkTrainerFolders = new List<string> { @"C:\blink" }, DeeperRecentFiles = new List<string> { @"C:\d.json" },
            DeeperLastDirectory = @"C:\deep", PlayerLevel = 99, FlashEnabled = false,
            DisabledAssetPaths = new HashSet<string> { "videos/w.mp4" },
            AudioLayers = new List<AudioLayerTrack> { new() { Path = @"C:\Users\me\layer.mp3" } },
            CornerGifOverlays = new List<CornerGifOverlaySetting> { new() { GifPath = @"C:\Users\me\corner.gif" } },
            KeywordTriggers = new List<KeywordTrigger> { new() { Id = "trigger", Keyword = "w", Actions = new List<KeywordAction> { new PlayAudioAction { Id = "audio", FilePath = @"C:\k.mp3" } } } },
        };
        _fake.Body = new JObject
        {
            ["backup"] = new JObject { ["settings_data"] = Pack(JsonConvert.SerializeObject(windows)), ["app_version"] = "6.11.0" },
        }.ToString();
        var here = Signed();
        here.PlayerLevel = 5;
        here.WallpaperSourceFolder = "/home/me/walls";

        var restored = await new CloudSettingsBackup(_fake).DownloadAsync(here);

        Assert.NotNull(restored);
        var (url, token, body) = Assert.Single(_fake.Seen);
        Assert.Equal(Url + "/v2/user/settings-backup", url);
        Assert.Equal("TOK", token);
        Assert.Equal("u_abc", (string?)JObject.Parse(body)["unified_id"]);
        foreach (var name in CloudSettingsBackup.AbsolutePathFields)
        {
            var p = typeof(AppSettings).GetProperty(name)!;
            Assert.Equal(JsonConvert.SerializeObject(p.GetValue(here)), JsonConvert.SerializeObject(p.GetValue(restored)));
        }
        Assert.Equal("/home/me/layer.mp3", Assert.Single(restored!.AudioLayers).Path);
        Assert.Equal("/home/me/corner.gif", Assert.Single(restored.CornerGifOverlays).GifPath);
        Assert.Equal("/home/me/k.mp3", ((PlayAudioAction)Assert.Single(Assert.Single(restored.KeywordTriggers).Actions)).FilePath);
        Assert.Equal(5, restored.PlayerLevel);
        Assert.Equal("u_abc", restored.UnifiedId);
        Assert.False(restored.FlashEnabled);                       // the backup's own settings do come back
        Assert.Contains("videos/w.mp4", restored.DisabledAssetPaths); // relative asset lists travel
        Assert.NotSame(here.BlinkTrainerFolders, restored.BlinkTrainerFolders);
    }

    [Fact]
    public async Task ListRestoreMatchesIdsAcrossReorderingAndClearsBackupOnlyPaths()
    {
        var here = Signed();
        here.KeywordTriggers[0].Actions.Add(new PlayAudioAction { Id = "audio2", FilePath = "/home/me/k2.mp3" });
        here.KeywordTriggers.Add(new KeywordTrigger { Id = "other", Keyword = "other",
            Actions = new List<KeywordAction> { new PlayAudioAction { Id = "other-audio", FilePath = "/local/other.mp3" } } });
        var backup = JObject.FromObject(here);
        backup["AudioLayers"]![0]!["Volume"] = 19;
        ((JArray)backup["AudioLayers"]!).Add(JObject.FromObject(new AudioLayerTrack { Path = @"C:\foreign.mp3", Volume = 33 }));
        ((JArray)backup["CornerGifOverlays"]!).Add(JObject.FromObject(new CornerGifOverlaySetting { GifPath = @"C:\foreign.gif", Size = 444 }));
        var triggers = (JArray)backup["KeywordTriggers"]!;
        var first = triggers[0]!;
        first.Remove();
        triggers.Add(first); // stable trigger id, not keyword or list index
        first["Keyword"] = "renamed";
        var actions = (JArray)first["Actions"]!;
        var audio = actions[0]!;
        audio.Remove();
        actions.Add(audio); // [AddXp, audio2, audio]: the two audio actions swap places, ids must win over position
        actions.Add(JObject.FromObject(new PlayAudioAction { Id = "new", FilePath = @"C:\new.mp3" }));
        triggers.Add(JObject.FromObject(new KeywordTrigger { Id = "new", AudioFilePath = @"C:\legacy.mp3",
            Actions = new List<KeywordAction> { new PlayAudioAction { Id = "audio", FilePath = @"C:\new.mp3" } } }));
        // Local-only entries must not survive when the backup carries a list.
        here.AudioLayers.Add(new AudioLayerTrack { Path = "/local/second.mp3" });
        here.AudioLayers.Add(new AudioLayerTrack { Path = "/local/only.mp3" });
        here.KeywordTriggers.Add(new KeywordTrigger { Id = "local-only" });
        _fake.Body = new JObject { ["backup"] = new JObject { ["settings_data"] = Pack(backup.ToString()) } }.ToString();

        var restored = (await new CloudSettingsBackup(_fake).DownloadAsync(here))!;

        Assert.Equal(new[] { "/home/me/layer.mp3", "/local/second.mp3" }, restored.AudioLayers.Select(x => x.Path));
        Assert.Equal(new[] { 19, 33 }, restored.AudioLayers.Select(x => x.Volume));
        Assert.Equal("", restored.CornerGifOverlays[1].GifPath);
        Assert.Equal(444, restored.CornerGifOverlays[1].Size);
        Assert.Equal(new[] { "other", "trigger", "new" }, restored.KeywordTriggers.Select(x => x.Id));
        Assert.Equal("/local/other.mp3", ((PlayAudioAction)restored.KeywordTriggers[0].Actions[0]).FilePath);
        var renamed = restored.KeywordTriggers[1];
        Assert.Equal("renamed", renamed.Keyword);
        Assert.Equal("/home/me/k2.mp3", ((PlayAudioAction)renamed.Actions[1]).FilePath);
        Assert.Equal("/home/me/k.mp3", ((PlayAudioAction)renamed.Actions[2]).FilePath);
        // No local action at that id or position: the local trigger's own AudioFilePath, never the foreign one.
        Assert.Equal("/home/me/legacy.mp3", ((PlayAudioAction)renamed.Actions[3]).FilePath);
        Assert.Equal("", restored.KeywordTriggers[2].AudioFilePath);
        Assert.Equal("", ((PlayAudioAction)restored.KeywordTriggers[2].Actions[0]).FilePath);
        Assert.DoesNotContain(@"C:\", JsonConvert.SerializeObject(restored));
        Assert.Equal(42, here.AudioLayers[0].Volume);
    }

    private async Task<AppSettings> RoundTrip(AppSettings here, Action<JObject>? edit = null)
    {
        var backup = JObject.FromObject(here);
        edit?.Invoke(backup);
        _fake.Body = new JObject { ["backup"] = new JObject { ["settings_data"] = Pack(backup.ToString()) } }.ToString();
        return (await new CloudSettingsBackup(_fake).DownloadAsync(here))!;
    }

    [Fact]
    public async Task DuplicateIdsKeepEachItemsOwnPathInsteadOfTheFirstMatch()
    {
        var here = Signed();
        here.KeywordTriggers = new List<KeywordTrigger>
        {
            new() { Id = "dup", AudioFilePath = "/local/1.mp3", Actions = new List<KeywordAction> { new PlayAudioAction { Id = "x", FilePath = "/local/1a.mp3" }, new PlayAudioAction { Id = "x", FilePath = "/local/1b.mp3" } } },
            new() { Id = "dup", AudioFilePath = "/local/2.mp3", Actions = new List<KeywordAction> { new PlayAudioAction { Id = "y", FilePath = "/local/2a.mp3" } } },
        };
        var restored = await RoundTrip(here, b =>
        {
            foreach (var trigger in (JArray)b["KeywordTriggers"]!)
            {
                trigger["AudioFilePath"] = @"C:\foreign.mp3";
                foreach (var action in (JArray)trigger["Actions"]!) action["filePath"] = @"C:\foreign.mp3";
            }
        });
        Assert.Equal(new[] { "/local/1.mp3", "/local/2.mp3" }, restored.KeywordTriggers.Select(t => t.AudioFilePath));
        Assert.Equal(new[] { "/local/1a.mp3", "/local/1b.mp3", "/local/2a.mp3" },
            restored.KeywordTriggers.SelectMany(t => t.Actions.OfType<PlayAudioAction>()).Select(a => a.FilePath));
    }

    [Fact]
    public async Task PresetTriggerPathsNeverUploadAndRestoreLocalByIdOrBlank()
    {
        var here = Signed();
        Assert.True(await new CloudSettingsBackup(_fake).BackupAsync(here));
        var sent = Unpack((string)JObject.Parse(Uploads.Single().Body)["settings_data"]!);
        var sentTrigger = sent["KeywordTriggerPresets"]![0]!["triggers"]![0]!;
        Assert.Null(sentTrigger["AudioFilePath"]);
        Assert.Null(sentTrigger["Actions"]![0]!["filePath"]);

        var restored = await RoundTrip(here, b =>
        {
            var presets = (JArray)b["KeywordTriggerPresets"]!;
            presets[0]!["triggers"]![0]!["AudioFilePath"] = @"C:\Users\foreign\legacy.mp3";
            presets[0]!["triggers"]![0]!["Actions"]![0]!["filePath"] = @"C:\Users\foreign\nested.mp3";
            presets.Add(JObject.FromObject(new KeywordTriggerPreset { Id = "foreign", Triggers = new List<KeywordTrigger> { new() { Id = "pt",
                AudioFilePath = @"C:\Users\foreign\x.mp3", Actions = new List<KeywordAction> { new PlayAudioAction { Id = "pa", FilePath = @"C:\Users\foreign\y.mp3" } } } } }));
        });
        var local = restored.KeywordTriggerPresets[0].Triggers[0];
        Assert.Equal("/home/me/preset-legacy.mp3", local.AudioFilePath);
        Assert.Equal("/home/me/preset.mp3", ((PlayAudioAction)local.Actions[0]).FilePath);
        Assert.Equal("", restored.KeywordTriggerPresets[1].Triggers[0].AudioFilePath);
        Assert.Equal("", ((PlayAudioAction)restored.KeywordTriggerPresets[1].Triggers[0].Actions[0]).FilePath);
        Assert.DoesNotContain(@"C:\", JsonConvert.SerializeObject(restored));
    }

    [Fact]
    public void UploadStrippingIgnoresJsonNameCasing()
    {
        var old = JsonConvert.DefaultSettings;
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
        try
        {
            var sent = CloudSettingsBackup.StripForUpload(Signed());
            Assert.NotNull(sent["audioLayers"]); // the camelCase policy really applied
            Assert.DoesNotContain("/home/me", sent.ToString());
        }
        finally { JsonConvert.DefaultSettings = old; }
    }

    [Fact]
    public async Task AnEditedLocalTriggerWithFreshActionIdsKeepsItsAudioPath()
    {
        var here = Signed();
        var backup = JObject.FromObject(here);
        // Every edit rebuilds the actions with new random ids (KeywordTrigger.RebuildActionsFromFlatFields).
        here.KeywordTriggers[0].RebuildActionsFromFlatFields();
        _fake.Body = new JObject { ["backup"] = new JObject { ["settings_data"] = Pack(backup.ToString()) } }.ToString();

        var restored = (await new CloudSettingsBackup(_fake).DownloadAsync(here))!;

        Assert.Equal("/home/me/legacy.mp3", Assert.Single(restored.KeywordTriggers[0].Actions.OfType<PlayAudioAction>()).FilePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingListsKeepLocalButExplicitEmptyListsClearThem(bool present)
    {
        var backup = present ? "{\"AudioLayers\":[],\"CornerGifOverlays\":[],\"KeywordTriggers\":[],\"KeywordTriggerPresets\":[]}" : "{}";
        _fake.Body = new JObject { ["backup"] = new JObject { ["settings_data"] = Pack(backup) } }.ToString();
        var here = Signed();
        var restored = (await new CloudSettingsBackup(_fake).DownloadAsync(here))!;
        Assert.Equal(present ? 0 : 1, restored.AudioLayers.Count);
        Assert.Equal(present ? 0 : 1, restored.CornerGifOverlays.Count);
        Assert.Equal(present ? 0 : 1, restored.KeywordTriggers.Count);
        Assert.Equal(present ? 0 : 1, restored.KeywordTriggerPresets.Count);
        if (!present)
        {
            Assert.Equal("/home/me/layer.mp3", restored.AudioLayers[0].Path);
            Assert.Equal("/home/me/corner.gif", restored.CornerGifOverlays[0].GifPath);
            Assert.Equal("/home/me/k.mp3", ((PlayAudioAction)restored.KeywordTriggers[0].Actions[0]).FilePath);
        }
    }

    [Fact]
    public async Task OverBudgetTrimsTheAssetListsFirstAndSkipsWhenStillTooBig()
    {
        var s = Signed();
        s.DisabledAssetPaths = new HashSet<string>(Enumerable.Range(0, 60_000).Select(_ => Guid.NewGuid() + ".png"));
        Assert.True(await new CloudSettingsBackup(_fake).BackupAsync(s));
        var data = (string)JObject.Parse(Uploads.Single().Body)["settings_data"]!;
        Assert.True(data.Length <= SettingsBackupBudget.MaxEncodedBytes);
        Assert.Null(Unpack(data)["DisabledAssetPaths"]);
        Assert.NotNull(Unpack(data)["AssetPresets"]);

        _fake.Seen.Clear();
        s.BannerSeenIds = Enumerable.Range(0, 60_000).Select(_ => Guid.NewGuid().ToString()).ToList();   // not trimmable
        Assert.False(await new CloudSettingsBackup(_fake).BackupAsync(s));
        Assert.Empty(Uploads);
    }

    /// <summary>Review R3 (fix-before-latest): the slot is shared with Windows and frozen WPF restores the whole blob,
    /// so a path field missing from a backup made here came back EMPTY on Windows (keyword triggers, audio layers,
    /// corner GIFs, spiral, wallpapers wiped). A backup made here carries the slot's own path fields unchanged.</summary>
    [Fact]
    public async Task ABackupMadeHereKeepsTheWindowsPathsAlreadyInTheSlot()
    {
        var windows = new AppSettings
        {
            SpiralPath = @"C:\Users\me\spiral.gif", StartupVideoPath = @"C:\v.mp4",
            AudioLayers = new List<AudioLayerTrack> { new() { Path = @"C:\Users\me\layer.mp3" } },
            KeywordTriggers = new List<KeywordTrigger> { new() { Id = "trigger", Keyword = "w", Actions = new List<KeywordAction> { new PlayAudioAction { Id = "audio", FilePath = @"C:\k.mp3" } } } },
            FlashEnabled = true,
        };
        _fake.BodyFor = u => u.EndsWith("/v2/user/settings-backup")
            ? new JObject { ["backup"] = new JObject { ["settings_data"] = Pack(JsonConvert.SerializeObject(windows)) } }.ToString()
            : null;
        var here = Signed();
        here.FlashEnabled = false;

        Assert.True(await new CloudSettingsBackup(_fake).BackupAsync(here));

        var sent = Unpack((string)JObject.Parse(Assert.Single(Uploads).Body)["settings_data"]!);
        Assert.DoesNotContain("/home/me", sent.ToString());                 // this machine's paths stay here
        Assert.Equal(@"C:\Users\me\spiral.gif", (string?)sent["SpiralPath"]);
        Assert.Equal(@"C:\v.mp4", (string?)sent["StartupVideoPath"]);
        var back = sent.ToObject<AppSettings>(JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace }))!;
        Assert.Equal(@"C:\Users\me\layer.mp3", Assert.Single(back.AudioLayers).Path);
        Assert.Equal(@"C:\k.mp3", Assert.Single(Assert.Single(back.KeywordTriggers).Actions.OfType<PlayAudioAction>()).FilePath);
        Assert.Equal("k", Assert.Single(back.KeywordTriggers).Keyword);
        Assert.Equal(17, Assert.Single(back.KeywordTriggers[0].Actions.OfType<AddXpAction>()).Amount);
        Assert.False(back.FlashEnabled);                                    // everything else is this machine's
    }

    /// <summary>Portable list edits travel with matched slot paths, never this machine's paths.</summary>
    [Fact]
    public async Task UploadCarriesOnlyMatchedSlotPathsAndKeepsPortableListEdits()
    {
        var slot = Signed();
        slot.AudioLayers[0].Path = @"C:\layer.mp3";
        slot.AudioLayers[0].Volume = 7;
        slot.CornerGifOverlays[0].GifPath = @"C:\corner.gif";
        slot.KeywordTriggers[0].AudioFilePath = @"C:\legacy.mp3";
        ((PlayAudioAction)slot.KeywordTriggers[0].Actions[0]).FilePath = @"C:\trigger.mp3";
        slot.KeywordTriggerPresets[0].Triggers[0].AudioFilePath = @"C:\preset.mp3";
        ((PlayAudioAction)slot.KeywordTriggerPresets[0].Triggers[0].Actions[0]).FilePath = @"C:\preset-action.mp3";
        var slotJson = JObject.FromObject(slot);
        foreach (var name in CloudSettingsBackup.AbsolutePathFields) slotJson.Remove(name);
        _fake.BodyFor = u => u.EndsWith("/v2/user/settings-backup")
            ? new JObject { ["backup"] = new JObject { ["settings_data"] = Pack(slotJson.ToString()) } }.ToString() : null;
        var here = Signed();
        here.AudioLayers.Add(new AudioLayerTrack { Path = "/home/me/new.mp3", Volume = 23 });
        here.KeywordTriggers[0].Keyword = "edited";
        here.KeywordTriggerPresets[0].Triggers[0].Keyword = "preset-edited";
        here.KeywordTriggers.Add(new KeywordTrigger { Id = "new", AudioFilePath = "/home/me/new.mp3" });
        Assert.True(await new CloudSettingsBackup(_fake).BackupAsync(here));
        var sent = Unpack((string)JObject.Parse(Assert.Single(Uploads).Body)["settings_data"]!);
        Assert.DoesNotContain("/home/me", sent.ToString());
        var back = sent.ToObject<AppSettings>(JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace }))!;
        Assert.Equal(new[] { @"C:\layer.mp3", "" }, back.AudioLayers.Select(t => t.Path));
        Assert.Equal(new[] { 42, 23 }, back.AudioLayers.Select(t => t.Volume));
        Assert.Equal(@"C:\corner.gif", back.CornerGifOverlays[0].GifPath);
        Assert.Equal(222, back.CornerGifOverlays[0].Size);
        Assert.Equal("edited", back.KeywordTriggers[0].Keyword);
        Assert.Equal(@"C:\legacy.mp3", back.KeywordTriggers[0].AudioFilePath);
        Assert.Equal(@"C:\trigger.mp3", ((PlayAudioAction)back.KeywordTriggers[0].Actions[0]).FilePath);
        Assert.Equal("", back.KeywordTriggers[1].AudioFilePath);
        Assert.Equal("preset-edited", back.KeywordTriggerPresets[0].Triggers[0].Keyword);
        Assert.Equal(@"C:\preset.mp3", back.KeywordTriggerPresets[0].Triggers[0].AudioFilePath);
        Assert.Equal(@"C:\preset-action.mp3", ((PlayAudioAction)back.KeywordTriggerPresets[0].Triggers[0].Actions[0]).FilePath);
    }

    /// <summary>When the slot cannot be read its paths cannot be kept: nothing is uploaded rather than wiping them.</summary>
    [Fact]
    public async Task ASlotThatCannotBeReadIsNeverOverwritten()
    {
        _fake.Status = HttpStatusCode.InternalServerError;
        Assert.False(await new CloudSettingsBackup(_fake).BackupAsync(Signed()));
        Assert.Empty(Uploads);
    }

    [Fact]
    public async Task NoTokenNoAccountOrOfflineSendsNothing()
    {
        var c = new CloudSettingsBackup(_fake);
        var s = Signed(); s.AuthToken = null;
        Assert.False(await c.BackupAsync(s));
        s = Signed(); s.UnifiedId = null;
        Assert.False(await c.BackupAsync(s));
        Assert.Null(await c.DownloadAsync(s));
        s = Signed(); s.OfflineMode = true;
        Assert.False(await c.BackupAsync(s));
        Assert.Empty(_fake.Seen);
    }

    [Fact]
    public async Task A409GoesToContractDAndOtherRefusalsFail()
    {
        var merged = 0;
        _fake.Status = HttpStatusCode.Conflict;
        _fake.Body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_new\"}";
        var c = new CloudSettingsBackup(_fake);
        Assert.False(await c.BackupAsync(Signed()));   // unseeded: an ordinary failure
        V2AuthService.MergedRecovery = (r, _) => { merged++; return Task.FromResult((int)r.StatusCode == 409); };
        Assert.False(await c.BackupAsync(Signed()));
        Assert.Null(await c.DownloadAsync(Signed()));
        Assert.Equal(2, merged);

        _fake.Status = HttpStatusCode.Unauthorized;
        Assert.False(await c.BackupAsync(Signed()));
        Assert.Null(await c.GetInfoAsync(Signed()));
    }

    [Fact]
    public async Task StatusReadsTheBackupsDateAndVersion()
    {
        _fake.Body = "{\"backup\":{\"backed_up_at\":\"2026-10-01T12:30:00Z\",\"app_version\":\"6.11.0\",\"size_bytes\":10}}";
        var info = await new CloudSettingsBackup(_fake).GetInfoAsync(Signed());
        Assert.Equal("6.11.0", info!.Value.AppVersion);
        Assert.Equal(new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc), info.Value.BackedUpAt!.Value.ToUniversalTime());
        _fake.Body = "{\"backup\":null}";
        Assert.Null(await new CloudSettingsBackup(_fake).GetInfoAsync(Signed()));
    }
}
