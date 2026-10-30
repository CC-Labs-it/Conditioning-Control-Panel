using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>One clip of a baked pack: a vertical PNG strip of <see cref="Frames"/> frames of W x H.</summary>
internal sealed record RainPackClip(string File, int Frames, int Fps, int W, int H);

/// <summary>A pack's manifest.json as read back (RAIN-V2-PLAN 2.3).</summary>
internal sealed record RainPackManifest(int Rev, string LookHash, string BakedAt, IReadOnlyDictionary<string, RainPackClip> Clips);

/// <summary>
/// Baked Rain looks on disk (RAIN-V2-PLAN 2.3, lane R7). One folder per look under
/// &lt;userdata&gt;/rain-packs/&lt;lookHash&gt;/: manifest.json, one &lt;clip&gt;.strip.png per clip, and
/// emotes.json + clip_timing.json folded from the embedded Resources/rain_emotes onto the clips the
/// pack has. A bake writes into &lt;hash&gt;.tmp and becomes visible only by one directory rename, so a
/// crash mid-bake leaves nothing a reader can pick up; the startup sweep drops the leftovers.
/// At most <see cref="Capacity"/> packs are kept, least recently used first out, and the pack
/// <see cref="AppSettings.RainPackHash"/> names is never evicted. Packs never sync.
/// </summary>
internal sealed class RainPackStore
{
    public const int PackRev = RainLookHash.PackRev;
    public const int Capacity = 3;
    public const string FolderName = "rain-packs";
    public const string ManifestName = "manifest.json";
    public const string TempSuffix = ".tmp";
    internal static readonly string[] TemplateFiles = { "emotes.json", "clip_timing.json" };

    private static readonly Regex ClipName = new("^[a-z0-9_]{1,40}$", RegexOptions.CultureInvariant);

    private readonly Func<string?> _currentHash;
    private readonly Func<string, string?> _readTemplate;
    private readonly Func<DateTime> _utcNow;

    public RainPackStore(string root, Func<string?> currentHash, Func<string, string?> readTemplate, Func<DateTime>? utcNow = null)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _currentHash = currentHash ?? (() => null);
        _readTemplate = readTemplate ?? throw new ArgumentNullException(nameof(readTemplate));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string Root { get; }

    /// <summary>The store under a user data root: &lt;userData&gt;/rain-packs.</summary>
    public static string RootFor(string userDataPath) => Path.Combine(userDataPath, FolderName);

    /// <summary>The app's store: App.UserDataPath (CCP_USERDATA_DIR aware), the current pack from
    /// settings, templates from the embedded Resources/rain_emotes.</summary>
    public static RainPackStore CreateDefault() =>
        new(RootFor(App.UserDataPath), () => App.Settings?.Current?.RainPackHash, ReadEmbeddedTemplate);

    private static string? ReadEmbeddedTemplate(string name)
    {
        var sri = System.Windows.Application.GetResourceStream(
            new Uri($"pack://application:,,,/Resources/rain_emotes/{name}", UriKind.Absolute));
        if (sri == null) return null;
        using var reader = new StreamReader(sri.Stream);
        return reader.ReadToEnd();
    }

    public static bool IsValidClipName(string? clip) => clip != null && ClipName.IsMatch(clip);

    /// <summary>Starts a bake: an empty &lt;hash&gt;.tmp folder (a stale one is cleared first).</summary>
    public RainPackWriter Begin(string lookHash)
    {
        if (!RainLookHash.IsWellFormed(lookHash)) throw new ArgumentException("Not a look hash.", nameof(lookHash));
        var temp = Path.Combine(Root, lookHash + TempSuffix);
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        return new RainPackWriter(this, lookHash, temp);
    }

    /// <summary>The absolute folder of a committed, readable pack, or null. A hit marks it used.</summary>
    public string? TryGetFolder(string? hash)
    {
        if (TryReadManifest(hash) == null) return null;
        var folder = Path.Combine(Root, hash!);
        try { Directory.SetLastWriteTimeUtc(folder, _utcNow()); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return folder;
    }

    /// <summary>The manifest of a committed pack, or null when absent, unreadable or inconsistent.</summary>
    public RainPackManifest? TryReadManifest(string? hash)
    {
        if (!RainLookHash.IsWellFormed(hash)) return null;
        var folder = Path.Combine(Root, hash!);
        var path = Path.Combine(folder, ManifestName);
        try
        {
            if (!File.Exists(path)) return null;
            var j = JObject.Parse(File.ReadAllText(path));
            if ((int?)j["rev"] != PackRev || (string?)j["lookHash"] != hash) return null;
            if (j["clips"] is not JObject clipsObj || !clipsObj.HasValues) return null;
            var clips = new Dictionary<string, RainPackClip>(StringComparer.Ordinal);
            foreach (var p in clipsObj.Properties())
            {
                if (!IsValidClipName(p.Name) || p.Value is not JObject c) return null;
                var clip = new RainPackClip((string?)c["file"] ?? "", (int?)c["frames"] ?? 0, (int?)c["fps"] ?? 0, (int?)c["w"] ?? 0, (int?)c["h"] ?? 0);
                if (clip.Frames <= 0 || clip.Fps <= 0 || clip.W <= 0 || clip.H <= 0) return null;
                if (clip.File != Path.GetFileName(clip.File) || !File.Exists(Path.Combine(folder, clip.File))) return null;
                clips[p.Name] = clip;
            }
            foreach (var t in TemplateFiles)
                if (!File.Exists(Path.Combine(folder, t))) return null;
            return new RainPackManifest(PackRev, hash!, (string?)j["bakedAt"] ?? "", clips);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidCastException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Startup sweep: removes *.tmp bakes and packs whose manifest does not read, then
    /// applies the cap. Returns how many folders went.</summary>
    public int Sweep()
    {
        if (!Directory.Exists(Root)) return 0;
        int removed = 0;
        foreach (var dir in Directory.GetDirectories(Root))
        {
            var name = Path.GetFileName(dir);
            bool orphan = name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);
            bool corrupt = !orphan && RainLookHash.IsWellFormed(name) && TryReadManifest(name) == null;
            if ((orphan || corrupt) && TryDelete(dir)) removed++;
        }
        return removed + Evict().Count;
    }

    /// <summary>Keeps the <see cref="Capacity"/> most recently used packs. The current pack and
    /// <paramref name="alsoKeep"/> (a pack just committed) are never evicted. Returns the hashes removed.</summary>
    public IReadOnlyList<string> Evict(string? alsoKeep = null)
    {
        if (!Directory.Exists(Root)) return Array.Empty<string>();
        var current = _currentHash();
        var packs = Directory.GetDirectories(Root)
            .Select(d => Path.GetFileName(d))
            .Where(RainLookHash.IsWellFormed)
            .ToList();
        bool Protected(string h) => string.Equals(h, current, StringComparison.Ordinal) || string.Equals(h, alsoKeep, StringComparison.Ordinal);
        var ordered = packs.Where(Protected)
            .Concat(packs.Where(h => !Protected(h)).OrderByDescending(h => Directory.GetLastWriteTimeUtc(Path.Combine(Root, h))))
            .ToList();
        var removed = new List<string>();
        foreach (var h in ordered.Skip(Capacity))
        {
            if (Protected(h)) continue;
            if (TryDelete(Path.Combine(Root, h))) removed.Add(h);
        }
        return removed;
    }

    internal string Commit(RainPackWriter w, string? lookJson)
    {
        if (w.Clips.Count == 0) throw new InvalidOperationException("A pack needs at least one clip.");
        foreach (var (name, clip) in w.Clips)
            if (!File.Exists(Path.Combine(w.TempFolder, clip.File)))
                throw new InvalidOperationException($"Clip {name} has no strip.");

        var names = new HashSet<string>(w.Clips.Keys, StringComparer.Ordinal);
        var emotes = _readTemplate("emotes.json") ?? throw new InvalidOperationException("emotes.json template missing.");
        var timing = _readTemplate("clip_timing.json") ?? throw new InvalidOperationException("clip_timing.json template missing.");
        File.WriteAllText(Path.Combine(w.TempFolder, "emotes.json"),
            RainPackEmotesFold.FoldEmotes(JObject.Parse(emotes), names).ToString(Formatting.Indented));
        File.WriteAllText(Path.Combine(w.TempFolder, "clip_timing.json"),
            RainPackEmotesFold.FoldTiming(JObject.Parse(timing), names).ToString(Formatting.Indented));

        var clipsObj = new JObject();
        foreach (var (name, c) in w.Clips.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            clipsObj[name] = new JObject { ["file"] = c.File, ["frames"] = c.Frames, ["fps"] = c.Fps, ["w"] = c.W, ["h"] = c.H };
        var manifest = new JObject
        {
            ["rev"] = PackRev,
            ["lookHash"] = w.LookHash,
            ["look"] = JToken.Parse(RainLookHash.Normalize(lookJson)),
            ["bakedAt"] = _utcNow().ToString("o", CultureInfo.InvariantCulture),
            ["clips"] = clipsObj,
        };
        // The manifest goes in last: a temp folder without one is never mistaken for a pack.
        File.WriteAllText(Path.Combine(w.TempFolder, ManifestName), manifest.ToString(Formatting.Indented));

        w.BeforeRenameForTest?.Invoke();

        var final = Path.Combine(Root, w.LookHash);
        if (Directory.Exists(final) && TryReadManifest(w.LookHash) != null)
        {
            // The same look baked again: a readable pack already serves it, keep that one.
            TryDelete(w.TempFolder);
        }
        else
        {
            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.Move(w.TempFolder, final);
        }
        try { Directory.SetLastWriteTimeUtc(final, _utcNow()); } catch (IOException) { }
        Evict(alsoKeep: w.LookHash);
        return final;
    }

    private static bool TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>A bake in progress: strips go into <see cref="TempFolder"/>, then <see cref="Commit"/>.
/// Disposing without a commit (cancel, close, panic) drops the temp folder.</summary>
internal sealed class RainPackWriter : IDisposable
{
    private readonly RainPackStore _store;
    private readonly Dictionary<string, RainPackClip> _clips = new(StringComparer.Ordinal);
    private bool _done;

    internal RainPackWriter(RainPackStore store, string lookHash, string tempFolder)
    {
        _store = store;
        LookHash = lookHash;
        TempFolder = tempFolder;
    }

    public string LookHash { get; }
    public string TempFolder { get; }
    public IReadOnlyDictionary<string, RainPackClip> Clips => _clips;

    /// <summary>Test seam: runs after every file is written, before the rename.</summary>
    internal Action? BeforeRenameForTest { get; set; }

    public static string StripFileName(string clip) => clip + ".strip.png";

    /// <summary>Where the strip for a clip is written.</summary>
    public string StripPath(string clip)
    {
        if (!RainPackStore.IsValidClipName(clip)) throw new ArgumentException("Bad clip name.", nameof(clip));
        return Path.Combine(TempFolder, StripFileName(clip));
    }

    /// <summary>Records a clip whose strip is already at <see cref="StripPath"/>.</summary>
    public void AddClip(string clip, int frames, int fps, int w, int h)
    {
        if (_done) throw new InvalidOperationException("The bake is finished.");
        if (frames <= 0 || fps <= 0 || w <= 0 || h <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        _clips[clip] = new RainPackClip(Path.GetFileName(StripPath(clip)), frames, fps, w, h);
    }

    /// <summary>Folds the emote maps, writes the manifest and renames the folder into place.
    /// Returns the pack's absolute folder.</summary>
    public string Commit(string? lookJson)
    {
        if (_done) throw new InvalidOperationException("The bake is finished.");
        var folder = _store.Commit(this, lookJson);
        _done = true;
        return folder;
    }

    public void Abort()
    {
        if (_done) return;
        _done = true;
        try { if (Directory.Exists(TempFolder)) Directory.Delete(TempFolder, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => Abort();
}

/// <summary>
/// Rewrites Rain's emotes.json / clip_timing.json for a pack that has only some of the 12 clips
/// (until the wardrobe GLB carries every action, a custom look bakes idle, greeting and jump).
/// Every clip the map names that the pack lacks folds onto one it has: talk, listening, shy and
/// smirk onto idle, giggle onto greeting, anything else onto idle, and failing idle onto the
/// pack's first clip. A full pack comes out unchanged.
/// </summary>
internal static class RainPackEmotesFold
{
    public static string Resolve(string clip, IReadOnlySet<string> have)
    {
        if (have.Contains(clip)) return clip;
        string? near = clip.StartsWith("talk", StringComparison.Ordinal) || clip.StartsWith("shy", StringComparison.Ordinal)
                       || clip is "listening" or "smirk" ? "idle"
                     : clip == "giggle" ? "greeting"
                     : null;
        if (near != null && have.Contains(near)) return near;
        if (have.Contains("idle")) return "idle";
        return have.OrderBy(c => c, StringComparer.Ordinal).First();
    }

    public static JObject FoldEmotes(JObject template, IReadOnlySet<string> have)
    {
        var j = (JObject)template.DeepClone();
        string R(string c) => Resolve(c, have);

        if (j["idleRotation"] is JArray idle)
        {
            var merged = new List<(string clip, int weight)>();
            foreach (var it in idle.OfType<JObject>())
            {
                var c = R((string?)it["clip"] ?? "idle");
                var w = (int?)it["weight"] ?? 1;
                var at = merged.FindIndex(m => m.clip == c);
                if (at >= 0) merged[at] = (c, merged[at].weight + w); else merged.Add((c, w));
            }
            j["idleRotation"] = new JArray(merged.Select(m => new JObject { ["clip"] = m.clip, ["weight"] = m.weight }));
        }
        if (j["talking"] is JObject talking) FoldList(talking, "pool", R);
        if (j["expressive"] is JObject expressive) FoldList(expressive, "pool", R);
        FoldList(j, "clickEmotes", R);
        foreach (var map in new[] { "stemPrefix", "mood" })
            if (j[map] is JObject o)
                foreach (var p in o.Properties().Where(p => p.Value.Type == JTokenType.String))
                    p.Value = R((string)p.Value!);
        if (j["clipScale"] is JObject scale)
            foreach (var p in scale.Properties().Where(p => !p.Name.StartsWith("_") && !have.Contains(p.Name)).ToList())
                p.Remove();
        return j;
    }

    public static JObject FoldTiming(JObject template, IReadOnlySet<string> have)
    {
        var j = (JObject)template.DeepClone();
        foreach (var p in j.Properties().Where(p => !p.Name.StartsWith("_") && !have.Contains(p.Name)).ToList())
            p.Remove();
        return j;
    }

    private static void FoldList(JObject owner, string key, Func<string, string> resolve)
    {
        if (owner[key] is not JArray arr) return;
        owner[key] = new JArray(arr.Where(t => t.Type == JTokenType.String).Select(t => resolve((string)t!)).Distinct());
    }
}
