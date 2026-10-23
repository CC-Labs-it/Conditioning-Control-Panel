// Adapted from parity/trunk 2d3523b2b0078ce8ebb3ccca88dbb2cef7228724 (WPF pool).
// ponytail: only ForGame is imported; peer and Sort pools have no caller here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;
namespace ConditioningControlPanel.Services.GoonGame.Online
{
    internal static class GoonOnlineMediaRules
    {
        private static readonly Regex NicheRx = new("^[a-zA-Z0-9_]{2,40}$", RegexOptions.CultureInvariant);
        public const int MaxSubs = 8;
        public const int StillTarget = 24;
        public const int ClipTarget = 12;
        public const int MaxWaves = 3;
        public static int Cap(int perWave) => perWave * MaxWaves;
        public const int Concurrency = 3;
        public const int MaxDryBatches = 3;
        public const int MaxCustomChars = 16 * 1024;
        public static readonly IReadOnlyList<string> Flavours =
            new[] { "trance", "pink", "frills", "shiny", "censored", "mine" };
        public static bool IsNiche(string? s) => s != null && NicheRx.IsMatch(s);
        public static List<string> CleanSubs(IEnumerable<string?>? raw)
        {
            var list = new List<string>();
            if (raw == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in raw)
            {
                var s = r?.Trim();
                if (s != null && s.StartsWith("r/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                if (!IsNiche(s) || !seen.Add(s!)) continue;
                list.Add(s!);
                if (list.Count >= MaxSubs) break;
            }
            return list;
        }
        public static string CleanFlavour(string? raw)
        {
            var s = (raw ?? "").Trim().ToLowerInvariant();
            return Flavours.Contains(s) ? s : "";
        }
        public static string CleanCustom(JToken? raw)
        {
            if (raw is not JObject o) return "";
            var s = o.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length <= MaxCustomChars ? s : "";
        }
        public static JObject ParseCustom(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return new JObject();
            try { return JToken.Parse(stored) as JObject ?? new JObject(); }
            catch { return new JObject(); }
        }
        public static string JoinSubs(IEnumerable<string> subs) => string.Join(",", subs);
        public static List<string> SplitSubs(string? stored)
            => CleanSubs((stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
        public static bool IsSessionOptIn(bool online, string flavour)
            => online && !string.IsNullOrEmpty(flavour);
        public static string StateFor(bool online, int subs, int have, bool running, bool failed)
        {
            if (!online) return "off";
            if (subs == 0) return "empty";
            if (have > 0) return running ? "loading" : "ready";
            if (running) return "loading";
            return failed ? "error" : "empty";
        }
        public static string? UrlFor(string? path, string? assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(assetsRoot)) return null;
            string full, root;
            try
            {
                full = Path.GetFullPath(path);
                root = Path.GetFullPath(assetsRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            }
            catch { return null; }
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            var rel = full.Substring(root.Length).Replace('\\', '/');
            var parts = rel.Split('/').Select(Uri.EscapeDataString);
            return "https://ccp.assets/" + string.Join("/", parts);
        }
    }
    internal sealed class GoonOnlineMedia : IDisposable
    {
        public sealed record Item(string Name, string Url, string Path);
        internal static Func<string, CancellationToken, Task<string?>> Materialize = ConditioningControlPanel.Services.Chaos.DtrhRemoteMedia.AssetsTempCopier.Materialize;
        internal static Action<string> ReleaseFile = ConditioningControlPanel.Services.Chaos.DtrhRemoteMedia.AssetsTempCopier.Release;
        private static Task<string?> MaterializeAsync(string url, CancellationToken ct) => Materialize(url, ct);
        public sealed record Snapshot(string State, IReadOnlyList<string> Subs,
            IReadOnlyList<Item> Images, IReadOnlyList<Item> Videos, int Have, int Want);
        private readonly string _prefix;
        private readonly int _stillTarget = GoonOnlineMediaRules.StillTarget;
        private readonly int _clipTarget = GoonOnlineMediaRules.ClipTarget;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> NamedChannels = new(StringComparer.Ordinal);
        private string StillTenant => _prefix + "-stills";
        private string ClipTenant => _prefix + "-clips";
        private IReadOnlyList<string> Channels() => NamedChannels.TryGetValue(_prefix, out var c) ? c : Array.Empty<string>();
        public static GoonOnlineMedia ForGame(string prefix, Action<Snapshot> onSnapshot) => new(prefix, onSnapshot);
        private GoonOnlineMedia(string prefix, Action<Snapshot> onSnapshot) { _prefix = prefix; _onSnapshot = onSnapshot; }
        private readonly object _gate = new();
        private readonly Action<Snapshot> _onSnapshot;
        private CancellationTokenSource? _cts;
        private int _gen;
        private bool _disposed;
        private List<string> _subs = new();
        private readonly List<Item> _images = new();
        private readonly List<Item> _videos = new();
        private bool _running;
        private bool _failed;
        private int _stillAdded;
        private int _clipAdded;
        private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
        // WPF wave sizes, cap and retirement delay; a niche change drops the previous deck.
        public void Off()
        {
            List<Item> drop;
            lock (_gate)
            {
                _gen++;
                CancelLocked();
                drop = TakeFilesLocked();
                _subs = new List<string>();
                _running = false;
                _failed = false;
            }
            Release(drop);
            _onSnapshot(new Snapshot("off", Array.Empty<string>(), Array.Empty<Item>(), Array.Empty<Item>(), 0, 0));
        }
        public void Start(IReadOnlyList<string> subs)
        {
            var clean = GoonOnlineMediaRules.CleanSubs(subs);
            List<Item>? drop = null;
            Snapshot? same = null;
            int gen = 0;
            CancellationToken ct = default;
            lock (_gate)
            {
                if (_disposed) return;
                if (clean.SequenceEqual(_subs, StringComparer.OrdinalIgnoreCase)
                    && (_running || _images.Count + _videos.Count > 0))
                {
                    same = SnapshotLocked();
                }
                else
                {
                    gen = ++_gen;
                    CancelLocked();
                    drop = TakeFilesLocked();
                    _subs = clean;
                    _failed = false;
                    _running = clean.Count > 0;
                    _stillAdded = 0;
                    _clipAdded = 0;
                    _seenIds.Clear();
                    _cts = new CancellationTokenSource();
                    ct = _cts.Token;
                }
            }
            if (same != null) { Raise(same); return; }
            Release(drop!);
            NamedChannels[_prefix] = clean;
            try
            {
                FypOnlineCoordinator.For(StillTenant, Channels, FeedMediaKind.GifStill).ResetChannels();
                FypOnlineCoordinator.For(ClipTenant, Channels, FeedMediaKind.GifClip).ResetChannels();
            }
            catch (Exception ex) { Log.Debug("GoonOnlineMedia: reset channels: {E}", ex.Message); }
            Emit(gen);
            if (clean.Count == 0) return;
            RunWave(gen, ct);
        }
        public bool More()
        {
            int gen;
            CancellationToken ct;
            lock (_gate)
            {
                if (_disposed || _running || _subs.Count == 0 || _cts == null) return false;
                gen = _gen;
                ct = _cts.Token;
                _running = true;
                _failed = false;
                _stillAdded = 0;
                _clipAdded = 0;
            }
            Emit(gen);
            RunWave(gen, ct);
            return true;
        }
        // The coordinator keeps its first channel provider: read the per-tenant map across relaunches.
        internal Task Pending { get; private set; } = Task.CompletedTask;
        internal static Func<string, IReadOnlyList<string>, FeedMediaKind, CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> Fetch =
            (tenant, channels, kind, ct) => FypOnlineCoordinator.For(tenant, () => NamedChannels[tenant[..tenant.LastIndexOf('-')]], kind).FetchBatchAsync(kind, ct);
        private void RunWave(int gen, CancellationToken ct)
        {
            Pending = Task.Run(async () =>
            {
                try
                {
                    var stills = FillAsync(gen, StillTenant, FeedMediaKind.GifStill, FeedMediaKind.Image,
                        _stillTarget, isImage: true, ct);
                    var clips = _clipTarget > 0
                        ? FillAsync(gen, ClipTenant, FeedMediaKind.GifClip, FeedMediaKind.GifClip,
                            _clipTarget, isImage: false, ct)
                        : Task.CompletedTask;
                    await Task.WhenAll(stills, clips).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Warning("GoonOnlineMedia: wave threw: {E}", ex.Message); }
                finally
                {
                    bool mine;
                    lock (_gate) { mine = gen == _gen; if (mine) _running = false; }
                    if (mine) Emit(gen);
                }
            });
        }
        private async Task FillAsync(int gen, string tenant, FeedMediaKind fetchKind, FeedMediaKind checkKind,
            int target, bool isImage, CancellationToken ct)
        {
            using var slots = new SemaphoreSlim(GoonOnlineMediaRules.Concurrency);
            int dry = 0;
            while (!ct.IsCancellationRequested && Added(isImage) < target && dry < GoonOnlineMediaRules.MaxDryBatches)
            {
                var (entries, error) = await Fetch(tenant, Channels(), fetchKind, ct).ConfigureAwait(false);
                if (error != null)
                {
                    lock (_gate) if (gen == _gen) _failed = true;
                    dry++;
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    continue;
                }
                List<FypAssetManifest.Entry> usable;
                lock (_gate)
                {
                    if (gen != _gen || ct.IsCancellationRequested) return;
                    usable = entries
                        .Where(e => RemoteMediaFormats.Validate(e, checkKind, out _) && e.Id != null && _seenIds.Add(e.Id))
                        .Take(Math.Max(0, target - (isImage ? _stillAdded : _clipAdded)))
                        .ToList();
                }
                if (usable.Count == 0) { dry++; continue; }
                int before = Added(isImage);
                var jobs = usable.Select(async e =>
                {
                    await slots.WaitAsync(ct).ConfigureAwait(false);
                    try { await MaterialiseOneAsync(gen, e, isImage, target, ct).ConfigureAwait(false); }
                    finally { slots.Release(); }
                });
                await Task.WhenAll(jobs).ConfigureAwait(false);
                dry = Added(isImage) > before ? 0 : dry + 1;
            }
        }
        private async Task MaterialiseOneAsync(int gen, FypAssetManifest.Entry e, bool isImage, int target,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested || Added(isImage) >= target) return;
            var path = await MaterializeAsync(e.Url, ct).ConfigureAwait(false);
            if (path == null) return;
            var url = GoonOnlineMediaRules.UrlFor(path, CorePaths.EffectiveAssets);
            if (url == null)
            {
                ReleaseFile(path);
                return;
            }
            bool kept = false;
            Item? retired = null;
            lock (_gate)
            {
                if (gen == _gen && !_disposed && !ct.IsCancellationRequested
                    && (isImage ? _stillAdded : _clipAdded) < target)
                {
                    var list = isImage ? _images : _videos;
                    if (list.Count >= GoonOnlineMediaRules.Cap(target))
                    {
                        retired = list[0];
                        list.RemoveAt(0);
                    }
                    list.Add(new Item(NameFor(e, isImage), url, path));
                    if (isImage) _stillAdded++; else _clipAdded++;
                    kept = true;
                }
            }
            if (!kept) { ReleaseFile(path); return; }
            if (retired != null) ReleaseLater(retired);
            Emit(gen);
        }
        private static string NameFor(FypAssetManifest.Entry e, bool isImage)
        {
            var id = e.Id ?? "";
            if (id.StartsWith("scrolller/", StringComparison.Ordinal)) id = id.Substring("scrolller/".Length);
            return (isImage ? "online:" : "online-clip:") + id;
        }
        private int Added(bool isImage)
        {
            lock (_gate) return isImage ? _stillAdded : _clipAdded;
        }
        // Leave a retired file alive for 30 s while the page replaces the element showing it.
        private static void ReleaseLater(Item item)
        {
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false); } catch { }
                Release(new[] { item });
            });
        }
        private void Emit(int gen)
        {
            Snapshot snap;
            lock (_gate)
            {
                if (gen != _gen || _disposed) return;
                snap = SnapshotLocked();
            }
            Raise(snap);
        }
        private void Raise(Snapshot s)
        {
            try { _onSnapshot(s); }
            catch (Exception ex) { Log.Debug("GoonOnlineMedia: snapshot sink threw: {E}", ex.Message); }
        }
        private Snapshot SnapshotLocked()
        {
            int have = _images.Count + _videos.Count;
            int want = _subs.Count == 0 ? 0
                : _running ? have + Math.Max(0, _stillTarget - _stillAdded)
                                 + Math.Max(0, _clipTarget - _clipAdded)
                           : Math.Max(have, _stillTarget + _clipTarget);
            var state = GoonOnlineMediaRules.StateFor(true, _subs.Count, have, _running, _failed);
            return new Snapshot(state, _subs.ToList(), _images.ToList(), _videos.ToList(), have, want);
        }
        private void CancelLocked()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }
        private List<Item> TakeFilesLocked()
        {
            var all = _images.Concat(_videos).ToList();
            _images.Clear();
            _videos.Clear();
            return all;
        }
        private static void Release(IEnumerable<Item> items)
        {
            foreach (var i in items)
            {
                try { ReleaseFile(i.Path); } catch { }
            }
        }
        public void Dispose()
        {
            List<Item> drop;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _gen++;
                CancelLocked();
                drop = TakeFilesLocked();
            }
            Release(drop);
        }
    }
}
