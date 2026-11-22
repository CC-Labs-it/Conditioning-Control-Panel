using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// The Llama Expo download (Contract v2 TRANSMISSION, CONTRACT.md 6.5). It starts only on the page's
/// <c>expo-fetch</c> (sent from a press), resumes through <see cref="ResumableDownload"/>, checks the
/// sha256 before the unpack, refuses zip-slip, writes the ready stamp last and maps the folder as
/// <c>https://ccp.expo/</c> in this window only. <c>expo-cancel</c>, close and panic stop it and keep
/// the partial. Nothing from the pack is ever run. Rules: <see cref="TcExpoRules"/>, <see cref="TcExpoPackRules"/>.
/// </summary>
internal static partial class TcHostService
{
    private static TcExpoState _expo = TcExpoState.Absent;
    private static TcExpoManifest? _expoManifest;
    private static CancellationTokenSource? _expoCts;
    private static int _expoRun;
    private static string? _expoServing;
    private static DateTime? _expoSentUtc;
    private static int _expoSentPct = -1;
    private static CoreWebView2? _core;
    private static readonly Lazy<HttpClient> ExpoHttpLazy = new(() =>
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}"); } catch (Exception ex) { Diag.Swallowed(ex); }
        return c;
    });

    private static string ContentRoot => ContentLocator.ContentRoot;

    private static HttpClient ExpoHttp() => ExpoHttpLazy.Value;

    private static (string[] Folders, string[] Stamps) ExpoDisk()
    {
        try
        {
            var root = TcExpoPackRules.Root(ContentRoot);
            if (!Directory.Exists(root)) return (Array.Empty<string>(), Array.Empty<string>());
            return (Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>().ToArray(),
                Directory.GetFiles(root, "*" + TcExpoPackRules.StampSuffix).Select(Path.GetFileName).OfType<string>().ToArray());
        }
        catch (Exception ex) { Diag.Swallowed(ex); return (Array.Empty<string>(), Array.Empty<string>()); }
    }

    /// <summary>The ready folder, when its stamp and root file are there.</summary>
    private static string? ExpoReadyFolder(out string? version)
    {
        var (folders, stamps) = ExpoDisk();
        version = TcExpoPackRules.ReadyVersion(folders, stamps);
        if (version == null) return null;
        var dir = TcExpoPackRules.VersionDir(ContentRoot, version);
        return File.Exists(Path.Combine(dir, TcExpoPackRules.RootProbe)) ? dir : null;
    }

    /// <summary>At launch (full edition): read the disk, map a ready pack, read the manifest in the
    /// background for the card's size. Returns the <c>ccp.expo</c> folder to map, or null.</summary>
    private static string? ExpoOnLaunch()
    {
        _expoSentUtc = null; _expoSentPct = -1; _expoServing = null;
        if (!TcRules.IsFull(_edition)) { _expo = TcExpoState.Absent; return null; }
        var folder = ExpoReadyFolder(out var version);
        _expo = TcExpoRules.FromDisk(folder != null, 0, 0);
        if (folder != null) { _expoServing = version; return folder; }
        if (App.Settings?.Current?.OfflineMode != true) _ = ReadManifestQuietlyAsync();
        return null;
    }

    private static async Task ReadManifestQuietlyAsync()
    {
        try { await ExpoManifestAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { App.Logger?.Debug("Tc: expo manifest read failed: {E}", ex.Message); }
    }

    private static async Task<TcExpoManifest?> ExpoManifestAsync(CancellationToken ct)
    {
        if (_expoManifest != null) return _expoManifest;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(45));
        var json = await ExpoHttp().GetStringAsync(TcExpoPackRules.ManifestUrl, cts.Token).ConfigureAwait(false);
        var m = TcExpoPackRules.Parse(json, UpdateService.AppVersion, out var why);
        if (m == null) App.Logger?.Warning("Tc: expo manifest refused ({Why})", why);
        return _expoManifest = m;
    }

    /// <summary><c>init.caps.expo</c> (full edition only).</summary>
    private static JObject? ExpoCap()
        => TcRules.IsFull(_edition) ? TcExpoRules.Cap(_expo, TcRules.ExpoBase, _expoManifest is { } m ? TcExpoPackRules.Mb(m.Bytes) : null) : null;

    private static void PostExpo(bool progress = false)
    {
        if (_host == null) return;
        if (progress)
        {
            var now = DateTime.UtcNow;
            if (!TcExpoRules.ShouldSendProgress(_expoSentUtc, _expoSentPct, now, _expo.Pct)) return;
            _expoSentUtc = now; _expoSentPct = _expo.Pct;
        }
        if (TcExpoRules.Frame(_expo, TcRules.ExpoBase) is { } f) _host.Post(f);
    }

    /// <summary><c>expo-fetch</c> (UI thread): start or resume.</summary>
    private static void OnExpoFetch()
    {
        var before = _expo;
        _expo = TcExpoRules.Start(_expo, TcRules.IsFull(_edition), App.Settings?.Current?.OfflineMode == true);
        if (_expo.Phase == TcExpoPhase.Failed) { PostExpo(); return; }
        if (_expo.Phase == TcExpoPhase.Ready) { PostExpo(); return; }
        if (before.Phase == TcExpoPhase.Fetching) return;   // already running
        _expoCts?.Dispose();
        _expoCts = new CancellationTokenSource();
        var run = ++_expoRun;
        var ct = _expoCts.Token;
        PostExpo(progress: true);
        _ = Task.Run(() => ExpoRunAsync(run, ct));
    }

    /// <summary><c>expo-cancel</c>, close and panic: stop, keep the partial. No frame (the page asked).</summary>
    private static void StopExpo()
    {
        _expoRun++;
        try { _expoCts?.Cancel(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _expo = TcExpoRules.Cancel(_expo);
    }

    private static void ExpoOnUi(int run, Func<TcExpoState, TcExpoState> step, bool progress = false)
        => RunOnUi(() =>
        {
            if (run != _expoRun) return;   // cancelled or a newer run
            _expo = step(_expo);
            PostExpo(progress);
        });

    private static async Task ExpoRunAsync(int run, CancellationToken ct)
    {
        try
        {
            TcExpoManifest? m;
            try { m = await ExpoManifestAsync(ct).ConfigureAwait(false); }
            catch (HttpRequestException) { ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonOffline)); return; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonOffline)); return; }
            if (m == null) { ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonRefused)); return; }

            var partial = TcExpoPackRules.PartialPath(ContentRoot, m);
            Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
            var have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (!HasExpoRoom(m, have)) { ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonDisk)); return; }

            // A folder without its stamp is absent: clean it (never the one a window serves).
            var (folders, stamps) = ExpoDisk();
            RemoveStale(folders, stamps, TcExpoPackRules.ReadyVersion(folders, stamps), _expoServing);

            var download = new ResumableDownload(ExpoHttp(), "TcExpo", 10);
            for (var pass = 1; ; pass++)
            {
                var ok = await download.ToPartialAsync(TcExpoPackRules.Tag, TcExpoPackRules.ReleaseBase + Uri.EscapeDataString(m.File),
                    m.Bytes, partial, (pct, _, _) => ExpoOnUi(run, s => TcExpoRules.Progress(s, (int)pct), progress: true), ct).ConfigureAwait(false);
                if (!ok) { ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonOffline)); return; }
                var sha = await ResumableDownload.Sha256Async(partial, ct).ConfigureAwait(false);
                if (sha == m.Sha256) break;
                App.Logger?.Warning("Tc: expo sha256 mismatch (pass {Pass})", pass);
                try { File.Delete(partial); } catch (Exception ex) { Diag.Swallowed(ex); }
                if (pass == 2) { ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonRefused)); return; }
            }

            ct.ThrowIfCancellationRequested();
            var why = await Task.Run(() => UnpackExpo(m, partial), ct).ConfigureAwait(false);
            if (why != null) { ExpoOnUi(run, s => TcExpoRules.Fail(s, why)); return; }
            try { File.Delete(partial); } catch (Exception ex) { Diag.Swallowed(ex); }

            RunOnUi(() =>
            {
                if (run != _expoRun) return;
                _expo = TcExpoRules.Complete(_expo);
                MapExpo(TcExpoPackRules.VersionDir(ContentRoot, m.Version), m.Version);
                PostExpo();
                // A newer version is ready and checked: the old folders go, never the one still served.
                var (f2, s2) = ExpoDisk();
                RemoveStale(f2, s2, m.Version, _expoServing);
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            App.Logger?.Information("Tc: expo download stopped, partial kept");
        }
        catch (IOException ex)
        {
            App.Logger?.Warning("Tc: expo download disk error: {E}", ex.Message);
            ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonDisk));
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Tc: expo download failed: {E}", ex.Message);
            ExpoOnUi(run, s => TcExpoRules.Fail(s, TcExpoRules.ReasonRefused));
        }
    }

    private static bool HasExpoRoom(TcExpoManifest m, long have)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(ContentRoot));
            if (string.IsNullOrEmpty(root)) return true;
            var drive = new DriveInfo(root);
            return !drive.IsReady || TcExpoPackRules.HasRoom(drive.AvailableFreeSpace, m, have);
        }
        catch (Exception ex) { Diag.Swallowed(ex); return true; }   // a probe that cannot answer says yes, as the packs
    }

    /// <summary>Unpack into a staging folder (every entry checked), then move it in and stamp it.
    /// Returns a <c>expo-failed</c> reason, or null when ready.</summary>
    private static string? UnpackExpo(TcExpoManifest m, string partial)
    {
        var stage = TcExpoPackRules.ExtractPath(ContentRoot, m);
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
                    if (target == null) { App.Logger?.Warning("Tc: expo zip entry refused (leaves the folder)"); return TcExpoRules.ReasonRefused; }
                    if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, overwrite: true);
                }
            }
            if (!File.Exists(Path.Combine(stage, TcExpoPackRules.RootProbe))) return TcExpoRules.ReasonRefused;

            var dir = TcExpoPackRules.VersionDir(ContentRoot, m.Version);
            Directory.CreateDirectory(TcExpoPackRules.Root(ContentRoot));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.Move(stage, dir);
            var stamp = TcExpoPackRules.StampPath(ContentRoot, m.Version);
            File.WriteAllText(stamp + ".tmp", new JObject { ["version"] = m.Version, ["sha256"] = m.Sha256 }.ToString());
            File.Move(stamp + ".tmp", stamp, overwrite: true);   // the stamp is written last
            return null;
        }
        catch (InvalidDataException) { return TcExpoRules.ReasonRefused; }
        catch (IOException ex) { App.Logger?.Warning("Tc: expo unpack disk error: {E}", ex.Message); return TcExpoRules.ReasonDisk; }
        catch (UnauthorizedAccessException) { return TcExpoRules.ReasonDisk; }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception ex) { Diag.Swallowed(ex); }
        }
    }

    private static void RemoveStale(string[] folders, string[] stamps, string? keep, string? serving)
    {
        var (dirs, files) = TcExpoPackRules.Stale(folders, stamps, keep, serving);
        var root = TcExpoPackRules.Root(ContentRoot);
        foreach (var s in files) { try { File.Delete(Path.Combine(root, s)); } catch (Exception ex) { Diag.Swallowed(ex); } }
        foreach (var d in dirs) { try { Directory.Delete(Path.Combine(root, d), true); } catch (Exception ex) { Diag.Swallowed(ex); } }
    }

    /// <summary><c>ccp.expo</c> on this window's core, read-only folder mapping like <c>ccp.assets</c>.
    /// The page only fetches from it (no navigation), so the navigation lock is unchanged.</summary>
    private static void MapExpo(string folder, string version)
    {
        if (_core == null || _host == null) return;
        try
        {
            _core.SetVirtualHostNameToFolderMapping(TcRules.ExpoHost, folder, CoreWebView2HostResourceAccessKind.Allow);
            _expoServing = version;
        }
        catch (Exception ex) { App.Logger?.Warning("Tc: ccp.expo mapping failed: {E}", ex.Message); }
    }
}
