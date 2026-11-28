// WPF ArcademyHostService.cs:643-661 (local-sample-request, probe-sub, share-image).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Quiz;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF ArcademyHostService.cs:4857-4863.
    private static string ArcademyTag(JObject o)
    {
        var raw = (o["tag"]?.Type == JTokenType.String ? (string?)o["tag"] ?? "" : "").Trim().ToLowerInvariant();
        var kept = new string(raw.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').Take(24).ToArray());
        return kept.Length == 0 ? "untagged" : kept;
    }

    // WPF ArcademyHostService.cs:5054-5185: a local pile sampled off the UI thread, answered as `assets` rows.
    internal Task? ArcademyLastLocalSample { get; private set; }
    private void ArcademyLocalSample(JObject o)
    {
        var reqId = (string?)o["reqId"] ?? "";
        int count;
        try { count = Math.Clamp((int?)o["count"] ?? 8, 1, 24); } catch { count = 8; }
        var kind = (string?)o["kind"] == "loop" ? "loop" : "still";
        var tag = ArcademyTag(o);
        var folders = (o["folders"] as JArray ?? new JArray()).Select(t => ((string?)t ?? "").Trim()).Where(f => f.Length > 0).ToList();
        var presetId = ((string?)o["presetId"] ?? "").Trim();
        ArcademyLastLocalSample = Task.Run(() =>
        {
            List<object> rows;
            // Root-relative: the page's isLocalUrl (provider/inventory.js:37) treats http://127.0.0.1 urls as remote.
            try { rows = ArcademySampleLocal(reqId, count, kind, tag, folders, presetId, WebAssetServer.AssetPath); }
            catch (Exception ex) { Log.Warning("[Arcademy] local sample failed: {E}", ex.Message); rows = new(); }
            Dispatcher.UIThread.Post(() => { if (!_closing) Post(new { type = "assets", reqId, tag, urls = rows, done = true }); });
        });
    }

    // WPF ArcademyHostService.cs:1498-1511: SORT's setup door (games/sort/setup.js:174) offers local piles
    // only from the folders and presets init.settings carries; each list is wrapped on its own.
    private static JObject ArcademySettingsBag(JObject bag)
    {
        try { bag["localAssets"] = ConditioningControlPanel.Services.Arcademy.ArcademyLocalAssets.Build(CorePaths.EffectiveAssets, CoreSettings.Current.DisabledAssetPaths, WebAssetServer.AssetPath); }
        catch (Exception ex) { Log.Debug("[Arcademy] local assets manifest failed: {E}", ex.Message); }
        try { bag["localFolders"] = ArcademyLocalFolders(); }
        catch (Exception ex) { Log.Debug("[Arcademy] local folders unavailable: {E}", ex.Message); }
        try
        {
            bag["assetPresets"] = new JArray((CoreSettings.Current.AssetPresets ?? new()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id))
                .Select(p => new JObject { ["id"] = p.Id, ["name"] = p.Name ?? "" }));
        }
        catch (Exception ex) { Log.Debug("[Arcademy] asset presets unavailable: {E}", ex.Message); }
        return bag;
    }

    // WPF ArcademyHostService.cs:1533-1604: media folders under images/ and videos/ with recursive
    // [gifs, stills, videos] counts, deselected files left out, at most 400 rows.
    private static JArray ArcademyLocalFolders()
    {
        var arr = new JArray();
        var root = CorePaths.EffectiveAssets;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return arr;
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var disabled = IntakeRun.DisabledAssetSet(CoreSettings.Current.DisabledAssetPaths);
        var counts = new SortedDictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        int[] Slot(string rel) => counts.TryGetValue(rel, out var slot) ? slot : counts[rel] = new int[3];
        foreach (var top in new[] { "images", "videos" })
        {
            var topAbs = Path.Combine(rootFull, top);
            if (!Directory.Exists(topAbs)) continue;
            Slot(top);   // an empty root is still a real choice ("all of my images")
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(topAbs, "*", SearchOption.AllDirectories); }
            catch (Exception ex) { Log.Debug("[Arcademy] local folders walk of {Top} failed: {E}", top, ex.Message); continue; }
            foreach (var file in files)
            {
                int bucket = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".gif" => 0, ".png" or ".jpg" or ".jpeg" or ".webp" => 1, ".mp4" or ".webm" => 2, _ => -1,
                };
                if (bucket < 0 || !IntakeRun.IsAssetActive(disabled, rootFull, file)) continue;
                // Credit the file to its own folder and every folder above it up to the root.
                for (var dir = Path.GetDirectoryName(file); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                {
                    var rel = Path.GetRelativePath(rootFull, dir).Replace('\\', '/');
                    if (rel.Length == 0 || rel == "." || rel.StartsWith("..", StringComparison.Ordinal)) break;
                    Slot(rel)[bucket]++;
                    if (string.Equals(rel, top, StringComparison.OrdinalIgnoreCase)) break;
                }
            }
        }
        foreach (var (rel, slot) in counts.Take(400))
            arr.Add(new JObject { ["path"] = rel, ["gifs"] = slot[0], ["stills"] = slot[1], ["videos"] = slot[2] });
        return arr;
    }

    private static List<object> ArcademySampleLocal(string reqId, int count, string kind, string tag,
        List<string> folders, string presetId, Func<string, string> assetUrl)
    {
        var rows = new List<object>();
        var root = CorePaths.EffectiveAssets;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return rows;
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        // WPF :5059-5060: .webp is in both lists, and its header settles which.
        var exts = kind == "loop" ? new[] { ".gif", ".mp4", ".webm", ".webp" } : new[] { ".png", ".jpg", ".jpeg", ".webp" };
        IEnumerable<string>? disabledPaths = CoreSettings.Current.DisabledAssetPaths;
        string? presetSrc = null;
        if (presetId.Length > 0 && CoreSettings.Current.AssetPresets?.FirstOrDefault(p => p != null && p.Id == presetId) is { } preset)
        {
            disabledPaths = preset.DisabledAssetPaths;
            presetSrc = "preset:" + preset.Id;
        }
        var disabled = IntakeRun.DisabledAssetSet(disabledPaths);
        // WPF ResolveAssetsFolder (:5195): a bridge path never climbs out of the assets root.
        var searchRoots = folders.Select(rel =>
        {
            try
            {
                var cleaned = rel.Replace('\\', '/').Trim('/');
                if (cleaned.Length == 0) return null;
                var abs = Path.GetFullPath(Path.Combine(rootFull, cleaned));
                bool inside = abs.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(abs, rootFull, StringComparison.OrdinalIgnoreCase);
                return inside && Directory.Exists(abs) ? abs : null;
            }
            catch { return null; }
        }).Where(d => d != null).Select(d => d!).ToList();
        if (searchRoots.Count == 0)
            searchRoots = new[] { "images", "videos" }.Select(t => Path.Combine(root, t)).Where(Directory.Exists).ToList();
        var pool = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in searchRoots)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    if (exts.Contains(Path.GetExtension(file).ToLowerInvariant()) && IntakeRun.IsAssetActive(disabled, root, file) && seen.Add(file))
                        pool.Add(file);
            }
            catch (Exception ex) { Log.Debug("[Arcademy] local sample walk of {Dir} failed: {E}", dir, ex.Message); }
        }
        // WPF :5148-5172: seeded off reqId (FNV-1a), partial Fisher-Yates consumed as it goes, 200 header probes.
        uint h = 2166136261;
        foreach (var ch in reqId) { h ^= ch; h *= 16777619; }
        var rng = new Random((int)(h & 0x7FFFFFFF));
        int probed = 0;
        for (int i = 0; i < pool.Count && rows.Count < count; i++)
        {
            int j = rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            var file = pool[i];
            var ext = Path.GetExtension(file).ToLowerInvariant();
            bool animated = ext == ".webp" && probed++ < 200 && ConditioningControlPanel.Services.Arcademy.ArcademyLocalAssets.IsAnimatedWebpFile(file);
            if (kind == "loop" && ext == ".webp" && !animated) continue;
            var rowKind = animated ? "loop" : kind;
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            // WPF AnimatedImageHint (:1678): the page's gif budgets read "#.gif"; the fetch drops the fragment.
            var url = assetUrl(rel) + (animated ? "#.gif" : "");
            var mime = ext switch
            {
                ".mp4" => "video/mp4", ".webm" => "video/webm", ".gif" => "image/gif",
                ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg",
            };
            var folder = Path.GetDirectoryName(rel.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? "";
            rows.Add(new { url, kind = rowKind, mime, tag, src = presetSrc ?? folder, poster = (string?)null });
        }
        return rows;
    }

    // WPF ArcademyHostService.cs:5281-5376: every reqId gets one sub-probe; a found sub joins the LIBRARY only,
    // never FypOnlineCustomSubs (FypRules.CommitProbe would put it in the app-wide feed).
    private readonly HashSet<string> _arcademyProbes = new(StringComparer.OrdinalIgnoreCase);
    internal Task? ArcademyLastProbe { get; private set; }
    private void ArcademyProbeSub(JObject o)
    {
        var reqId = (string?)o["reqId"] ?? "";
        var raw = (string?)o["name"] ?? (string?)o["sub"] ?? "";
        void Reply(string name, bool ok, int? videoCount, string? error) => Post(new
        {
            type = "sub-probe", reqId, name, ok, videoCount, stillOnly = ok && videoCount.GetValueOrDefault() == 0, error,
        });
        void PushLibrary() => Post(new { type = "library", subLibrary = FypRules.BuildLibraryPayload(CoreSettings.Current) });
        var clean = FypOnlineCoordinator.SanitizeSub(raw);
        if (clean == null) { Reply(raw.Trim(), false, null, "invalid"); return; }
        var s = CoreSettings.Current;
        if (!s.SubVerdictIsStale(clean) && s.FypOnlineSubVerdicts.TryGetValue(clean, out var cached) && cached != null)
        {
            if (cached.Ok && s.TryAddLibrarySub(clean)) { CoreSettings.Save(); PushLibrary(); }
            Reply(clean, cached.Ok, cached.VideoCount, null);
            return;
        }
        if (!ArcademyRemoteAllowed()) { Reply(clean, false, null, "offline"); return; }
        lock (_arcademyProbes)
            if (!_arcademyProbes.Add(clean)) { Reply(clean, false, null, "busy"); return; }
        ArcademyLastProbe = Task.Run(async () =>
        {
            try
            {
                var run = FypProbeOverride ?? ((sub, ct) => FypOnlineCoordinator.ProbeSubAsync(sub, ct));
                var probe = await run(clean, CancellationToken.None).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_closing) return;
                    var st = CoreSettings.Current;
                    bool moved = false;
                    // A transport failure taught us nothing about the sub, so no verdict is written.
                    if (probe.Error == null)
                    {
                        st.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict { Ok = probe.Ok, VideoCount = probe.VideoCount, CheckedAtUtc = DateTime.UtcNow };
                        moved = probe.Ok && st.TryAddLibrarySub(clean);
                        CoreSettings.Save();
                    }
                    Reply(clean, probe.Ok, probe.VideoCount, probe.Error);
                    if (moved) PushLibrary();
                });
            }
            catch (Exception ex)
            {
                Log.Warning("[Arcademy] sub probe failed: {E}", ex.Message);
                Dispatcher.UIThread.Post(() => { if (!_closing) Reply(clean, false, null, "offline"); });
            }
            finally { lock (_arcademyProbes) _arcademyProbes.Remove(clean); }
        });
    }

    // WPF ArcademyHostService.cs:5525-5600: the report card's PNG onto the clipboard, exactly one reply.
    internal Task? ArcademyLastShare { get; private set; }
    private void ArcademyShareImage(string? png) => ArcademyLastShare = Dispatcher.UIThread.InvokeAsync(async () =>
    {
        bool ok = false;
        try
        {
            if (!string.IsNullOrEmpty(png) && png.Length <= 4_400_000 && Clipboard is { } clip)
            {
                var bytes = Convert.FromBase64String(png);
                // The PNG signature is checked before anything decodes it.
                if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                {
                    using var ms = new MemoryStream(bytes, writable: false);
                    await clip.SetBitmapAsync(new Bitmap(ms));
                    ok = true;
                }
            }
        }
        catch (Exception ex) { Log.Debug("[Arcademy] share image refused: {E}", ex.Message); }
        if (!_closing) Post(new { type = "share-image-result", ok });
    });
}
