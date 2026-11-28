// PORTED from ConditioningControlPanel/Services/Tc/TcHostService.Expo.cs (main 6dca5d6d5b, 1dbdeaebae): the Llama Expo
// download (Contract v2 TRANSMISSION). It starts only on the page's expo-fetch, resumes through Core ResumableDownload,
// checks the sha256 before the unpack, refuses zip-slip, writes the ready stamp last. expo-cancel, close and panic stop
// it and keep the partial. Nothing from the pack is ever run. WPF maps the folder as https://ccp.expo/; here it is
// WebAssetServer's "ccp.expo/" prefix on the page's own loopback origin, so the base is http (see the ledger row).
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>Test seam: the handler under every Expo request (manifest and zip).</summary>
        internal static Func<HttpMessageHandler> TcExpoHandler { get; set; } = () => new HttpClientHandler();

        /// <summary>Test seam: where packs live (WPF ContentLocator.ContentRoot).</summary>
        internal static Func<string> TcExpoContentRoot { get; set; } = () => ContentLocator.ContentRoot;

        private TcExpoState _tcExpo = TcExpoState.Absent;
        private TcExpoManifest? _tcExpoManifest;
        private CancellationTokenSource? _tcExpoCts;
        private int _tcExpoRun;
        private string? _tcExpoServing;
        private DateTime? _tcExpoSentUtc;
        private int _tcExpoSentPct = -1;
        private DateTime _tcExpoOpenedUtc;
        private Task? _tcExpoSizeRead;

        internal TcExpoState TcExpo => _tcExpo;
        private static string TcExpoRoot => TcExpoContentRoot();
        private string TcExpoBase => Server().ExpoBase;

        /// <summary>Records the last HTTP status (redirects followed), so a 404 on the zip reads as refused.</summary>
        private sealed class TcExpoStatus : DelegatingHandler
        {
            public int? Last;
            public TcExpoStatus() : base(TcExpoHandler()) { }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var r = await base.SendAsync(request, ct).ConfigureAwait(false);
                Last = (int)r.StatusCode;
                return r;
            }
        }

        private static HttpClient TcExpoClient(HttpMessageHandler handler)
        {
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}"); } catch (Exception ex) { Diag.Swallowed(ex); }
            return c;
        }

        private static (string[] Folders, string[] Stamps) TcExpoDisk()
        {
            try
            {
                var root = TcExpoPackRules.Root(TcExpoRoot);
                if (!Directory.Exists(root)) return (Array.Empty<string>(), Array.Empty<string>());
                return (Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>().ToArray(),
                    Directory.GetFiles(root, "*" + TcExpoPackRules.StampSuffix).Select(Path.GetFileName).OfType<string>().ToArray());
            }
            catch (Exception ex) { Diag.Swallowed(ex); return (Array.Empty<string>(), Array.Empty<string>()); }
        }

        /// <summary>WPF ExpoOnLaunch: read the disk, serve a ready pack, take the card's size from the cached manifest
        /// and refresh it in the background (full edition only).</summary>
        private void TcExpoOnLaunch()
        {
            _tcExpoSentUtc = null; _tcExpoSentPct = -1; _tcExpoServing = null;
            _tcExpoOpenedUtc = DateTime.UtcNow;
            _tcExpoSizeRead = null;
            if (!TcRules.IsFull(_tcEdition)) { _tcExpo = TcExpoState.Absent; return; }
            var (folders, stamps) = TcExpoDisk();
            var version = TcExpoPackRules.ReadyVersion(folders, stamps);
            var dir = version == null ? null : TcExpoPackRules.VersionDir(TcExpoRoot, version);
            var ready = dir != null && File.Exists(Path.Combine(dir, TcExpoPackRules.RootProbe));
            _tcExpo = TcExpoRules.FromDisk(ready, 0, 0);
            if (ready) { ServeTcExpo(dir!, version!); return; }
            _tcExpoManifest ??= ReadCachedTcExpoManifest();
            if (!CoreSettings.Current.OfflineMode) _tcExpoSizeRead = Task.Run(async () =>
            {
                try { await ReadLiveTcExpoManifestAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Debug("Tc: expo manifest read failed: {E}", ex.Message); }
            });
        }

        /// <summary>WPF ExpoInitHold: how long the init waits for a first manifest read (at most 1.5 s from open).</summary>
        internal TimeSpan TcExpoInitHold()
            => TcExpoPackRules.InitHold(TcRules.IsFull(_tcEdition) && _tcExpo.Phase != TcExpoPhase.Ready, _tcExpoManifest != null,
                _tcExpoSizeRead?.IsCompleted != false, DateTime.UtcNow - _tcExpoOpenedUtc);

        private static TcExpoManifest? ReadCachedTcExpoManifest()
        {
            try
            {
                var path = TcExpoPackRules.CachePath(TcExpoRoot);
                if (!File.Exists(path) || new FileInfo(path).Length > TcExpoPackRules.MaxManifestChars) return null;
                return TcExpoPackRules.Parse(File.ReadAllText(path), CoreReleaseContent.AppVersion, out _);
            }
            catch (Exception ex) { Diag.Swallowed(ex); return null; }
        }

        /// <summary>The live manifest of the tag. A good one replaces the size and the cache.</summary>
        private async Task<(TcExpoManifest? Manifest, string? Fail)> ReadLiveTcExpoManifestAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                using var http = TcExpoClient(TcExpoHandler());
                using var resp = await http.GetAsync(TcExpoPackRules.ManifestUrl, cts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return (null, TcExpoPackRules.FailForStatus((int)resp.StatusCode));
                var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var m = TcExpoPackRules.Parse(json, CoreReleaseContent.AppVersion, out var why);
                if (m == null) { Log.Warning("Tc: expo manifest refused ({Why})", why); return (null, TcExpoRules.ReasonRefused); }
                _tcExpoManifest = m;
                try
                {
                    var path = TcExpoPackRules.CachePath(TcExpoRoot);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path + ".tmp", TcExpoPackRules.CacheJson(m));
                    File.Move(path + ".tmp", path, overwrite: true);
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
                return (m, null);
            }
            catch (HttpRequestException) { return (null, TcExpoRules.ReasonOffline); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return (null, TcExpoRules.ReasonOffline); }
        }

        /// <summary><c>init.caps.expo</c> (full edition only).</summary>
        private JObject? TcExpoCap()
            => TcRules.IsFull(_tcEdition) ? TcExpoRules.Cap(_tcExpo, TcExpoBase, _tcExpoManifest is { } m ? TcExpoPackRules.Mb(m.Bytes) : null) : null;

        private void PostTcExpo(bool progress = false)
        {
            if (!_tcOpen || _closing) return;
            if (progress)
            {
                var now = DateTime.UtcNow;
                if (!TcExpoRules.ShouldSendProgress(_tcExpoSentUtc, _tcExpoSentPct, now, _tcExpo.Pct)) return;
                _tcExpoSentUtc = now; _tcExpoSentPct = _tcExpo.Pct;
            }
            if (TcExpoRules.Frame(_tcExpo, TcExpoBase) is { } f) Post(f);
        }

        /// <summary><c>expo-fetch</c> (UI thread): start or resume.</summary>
        private void OnTcExpoFetch()
        {
            var before = _tcExpo;
            _tcExpo = TcExpoRules.Start(_tcExpo, TcRules.IsFull(_tcEdition), CoreSettings.Current.OfflineMode);
            if (_tcExpo.Phase is TcExpoPhase.Failed or TcExpoPhase.Ready) { PostTcExpo(); return; }
            if (before.Phase == TcExpoPhase.Fetching) return;   // already running
            _tcExpoCts?.Dispose();
            _tcExpoCts = new CancellationTokenSource();
            var run = ++_tcExpoRun;
            var ct = _tcExpoCts.Token;
            PostTcExpo(progress: true);
            _ = Task.Run(() => TcExpoRunAsync(run, ct));
        }

        /// <summary><c>expo-cancel</c>, close and panic: stop, keep the partial. No frame (the page asked).</summary>
        private void StopTcExpo()
        {
            _tcExpoRun++;
            try { _tcExpoCts?.Cancel(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _tcExpo = TcExpoRules.Cancel(_tcExpo);
        }

        private void TcExpoOnUi(int run, Func<TcExpoState, TcExpoState> step, bool progress = false)
            => Dispatcher.UIThread.Post(() =>
            {
                if (run != _tcExpoRun) return;   // cancelled or a newer run
                _tcExpo = step(_tcExpo);
                PostTcExpo(progress);
            });

        private async Task TcExpoRunAsync(int run, CancellationToken ct)
        {
            try
            {
                // The cached manifest is only the size: a download reads the live one (file, bytes, sha256).
                var cached = _tcExpoManifest;
                var (live, liveFail) = await ReadLiveTcExpoManifestAsync(ct).ConfigureAwait(false);
                var (m, fail) = TcExpoPackRules.ForDownload(cached, live, liveFail);
                if (m == null) { TcExpoOnUi(run, s => TcExpoRules.Fail(s, fail)); return; }

                var partial = TcExpoPackRules.PartialPath(TcExpoRoot, m);
                Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
                var have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (!HasTcExpoRoom(m, have)) { TcExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonDisk)); return; }

                // A folder without its stamp is absent: clean it (never the one this window serves).
                var (folders, stamps) = TcExpoDisk();
                RemoveStaleTcExpo(folders, stamps, TcExpoPackRules.ReadyVersion(folders, stamps), _tcExpoServing);

                var status = new TcExpoStatus();
                using var http = TcExpoClient(status);
                var download = new ResumableDownload(http, "TcExpo", 10);
                for (var pass = 1; ; pass++)
                {
                    status.Last = null;
                    var ok = await download.ToPartialAsync(TcExpoPackRules.Tag, TcExpoPackRules.ReleaseBase + Uri.EscapeDataString(m.File),
                        m.Bytes, partial, (pct, _, _) => TcExpoOnUi(run, s => TcExpoRules.Progress(s, (int)pct), progress: true), ct).ConfigureAwait(false);
                    if (!ok) { var why404 = TcExpoPackRules.FailForStatus(status.Last); TcExpoOnUi(run, s => TcExpoRules.Fail(s, why404)); return; }
                    var sha = await ResumableDownload.Sha256Async(partial, ct).ConfigureAwait(false);
                    if (sha == m.Sha256) break;
                    Log.Warning("Tc: expo sha256 mismatch (pass {Pass})", pass);
                    try { File.Delete(partial); } catch (Exception ex) { Diag.Swallowed(ex); }
                    if (pass == 2) { TcExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonRefused)); return; }
                }

                ct.ThrowIfCancellationRequested();
                var why = await Task.Run(() => UnpackTcExpo(m, partial), ct).ConfigureAwait(false);
                if (why != null) { TcExpoOnUi(run, s => TcExpoRules.Fail(s, why)); return; }
                try { File.Delete(partial); } catch (Exception ex) { Diag.Swallowed(ex); }

                Dispatcher.UIThread.Post(() =>
                {
                    if (run != _tcExpoRun) return;
                    _tcExpo = TcExpoRules.Complete(_tcExpo);
                    if (_tcOpen && !_closing) ServeTcExpo(TcExpoPackRules.VersionDir(TcExpoRoot, m.Version), m.Version);
                    PostTcExpo();
                    // A newer version is ready and checked: the old folders go, never the one still served.
                    var (f2, s2) = TcExpoDisk();
                    RemoveStaleTcExpo(f2, s2, m.Version, _tcExpoServing);
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Log.Information("Tc: expo download stopped, partial kept");
            }
            catch (IOException ex)
            {
                Log.Warning("Tc: expo download disk error: {E}", ex.Message);
                TcExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonDisk));
            }
            catch (Exception ex)
            {
                Log.Warning("Tc: expo download failed: {E}", ex.Message);
                TcExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonRefused));
            }
        }

        private static bool HasTcExpoRoom(TcExpoManifest m, long have)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(TcExpoRoot));
                if (string.IsNullOrEmpty(root)) return true;
                var drive = new DriveInfo(root);
                return !drive.IsReady || TcExpoPackRules.HasRoom(drive.AvailableFreeSpace, m, have);
            }
            catch (Exception ex) { Diag.Swallowed(ex); return true; }   // a probe that cannot answer says yes, as the packs
        }

        /// <summary>Unpack into a staging folder (every entry checked), then move it in and stamp it.
        /// Returns an <c>expo-failed</c> reason, or null when ready.</summary>
        private static string? UnpackTcExpo(TcExpoManifest m, string partial)
        {
            var stage = TcExpoPackRules.ExtractPath(TcExpoRoot, m);
            try
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                Directory.CreateDirectory(stage);
                using (var zip = ZipFile.OpenRead(partial))
                {
                    if (!TcExpoPackRules.UnpackFits(zip.Entries.Sum(e => e.Length), m)) return TcExpoRules.ReasonRefused;
                    foreach (var e in zip.Entries)
                    {
                        var target = TcExpoPackRules.EntryTarget(stage, e.FullName);
                        if (target == null) { Log.Warning("Tc: expo zip entry refused (leaves the folder)"); return TcExpoRules.ReasonRefused; }
                        if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        e.ExtractToFile(target, overwrite: true);
                    }
                }
                if (!File.Exists(Path.Combine(stage, TcExpoPackRules.RootProbe))) return TcExpoRules.ReasonRefused;

                var dir = TcExpoPackRules.VersionDir(TcExpoRoot, m.Version);
                Directory.CreateDirectory(TcExpoPackRules.Root(TcExpoRoot));
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.Move(stage, dir);
                var stamp = TcExpoPackRules.StampPath(TcExpoRoot, m.Version);
                File.WriteAllText(stamp + ".tmp", new JObject { ["version"] = m.Version, ["sha256"] = m.Sha256 }.ToString());
                File.Move(stamp + ".tmp", stamp, overwrite: true);   // the stamp is written last
                return null;
            }
            catch (InvalidDataException) { return TcExpoRules.ReasonRefused; }
            catch (IOException ex) { Log.Warning("Tc: expo unpack disk error: {E}", ex.Message); return TcExpoRules.ReasonDisk; }
            catch (UnauthorizedAccessException) { return TcExpoRules.ReasonDisk; }
            finally
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
        }

        private static void RemoveStaleTcExpo(string[] folders, string[] stamps, string? keep, string? serving)
        {
            var (dirs, files) = TcExpoPackRules.Stale(folders, stamps, keep, serving);
            var root = TcExpoPackRules.Root(TcExpoRoot);
            foreach (var s in files) { try { File.Delete(Path.Combine(root, s)); } catch (Exception ex) { Diag.Swallowed(ex); } }
            foreach (var d in dirs) { try { Directory.Delete(Path.Combine(root, d), true); } catch (Exception ex) { Diag.Swallowed(ex); } }
        }

        /// <summary>WPF MapExpo: the folder behind WebAssetServer's ccp.expo/ while this window is open.</summary>
        private void ServeTcExpo(string folder, string version)
        {
            WebAssetServer.ExpoFolder = folder;
            _tcExpoServing = version;
        }

        /// <summary>Close and panic (WPF Close: StopExpo, then the mapping goes with the core).</summary>
        private void CloseTcExpo()
        {
            StopTcExpo();
            if (_tcExpoServing != null) WebAssetServer.ExpoFolder = null;
            _tcExpoServing = null;
        }
    }
}
