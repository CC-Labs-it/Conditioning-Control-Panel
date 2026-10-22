using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Chromium's own disk cache under the licence day limit (owner, 2026-10-21). The gate's own
/// answers carry no-store, but clips and ranged requests pass straight to the network
/// (<see cref="ScrolllerWebRoute.PassCharged"/>) and Chromium keeps them in the profile's disk
/// cache. So every profile a gated core uses is cleared (DiskCache only: a page's localStorage and
/// IndexedDB, the Goon prefs, survive) when its last clear is a day old: at attach, hourly from
/// <see cref="ScrolllerCopySweeper"/>, and always on the off switch. A profile whose windows are
/// all closed has its cache folder deleted on disk instead. The last clear of each profile is kept
/// in <c>{EffectiveAssetsPath}/.scrolller-cache/webview-clears.json</c>.
///
/// Builds before this one cached clips with no stamp anywhere, so every pass also walks every
/// user data folder on disk (<see cref="KnownProfileFolders"/>): one with no stamp and no live core
/// is due and closed, and its cache folder goes on disk at once.
/// </summary>
internal static partial class ScrolllerWebGate
{
    internal const string ClearsFileName = "webview-clears.json";

    private sealed class LiveCore
    {
        public required WeakReference<CoreWebView2> Core;
        public required Dispatcher Dispatcher;
        /// <summary>The user data folder's name (browser_data_fyp, ...): one clear per profile.</summary>
        public required string Profile;
        public required string Folder;
    }

    internal readonly record struct ClearStamp(DateTime? At, string Folder);

    private static readonly List<LiveCore> Live = new();
    private static readonly object ClearsLock = new();
    private static readonly TimeSpan ClearBudget = TimeSpan.FromSeconds(30);

    /// <summary>Where the last clears are kept, or null for nowhere. Replaceable by tests.</summary>
    internal static Func<string?> ClearsPathProvider = () =>
    {
        var root = ScrolllerDiskCache.RootProvider();
        return string.IsNullOrEmpty(root) ? null : Path.Combine(root, ClearsFileName);
    };

    /// <summary>Where WebView2 user data folders live: every child of these that holds an EBWebView
    /// folder is a profile (browser_data_fyp, arcademy, backroom, ...). Replaceable by tests.</summary>
    internal static Func<IEnumerable<string>> ProfileRootsProvider = () =>
    {
        try { return new[] { App.UserDataPath }; }
        catch { return Array.Empty<string>(); }
    };

    /// <summary>User data folders kept outside those roots (the video engine's). Replaceable by tests.</summary>
    internal static Func<IEnumerable<string>> ExtraProfileFoldersProvider = () =>
    {
        try
        {
            return new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ConditioningControlPanel", "browser_data_video"),
            };
        }
        catch { return Array.Empty<string>(); }
    };

    /// <summary>Every WebView2 user data folder on disk, by full path.</summary>
    internal static IReadOnlyList<string> KnownProfileFolders()
    {
        var all = new List<string>();
        try
        {
            foreach (var root in ProfileRootsProvider())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                    if (Directory.Exists(Path.Combine(dir, "EBWebView"))) all.Add(dir);
            }
        }
        catch { }
        try
        {
            foreach (var dir in ExtraProfileFoldersProvider())
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(Path.Combine(dir, "EBWebView"))) all.Add(dir);
        }
        catch { }
        return all.Select(d => Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The clock for clear stamps. Replaceable by tests.</summary>
    internal static Func<DateTime> ClearsUtcNow = () => DateTime.UtcNow;

    /// <summary>The rule, pure: a profile never cleared (no stamp) is due, and so is one whose last
    /// clear will be a day old before the next hourly sweep, so nothing Chromium cached outlives
    /// the day. A stamp from the future (the clock went back) is due too.</summary>
    internal static bool ChromiumClearDue(DateTime? lastUtc, DateTime nowUtc)
        => lastUtc is not DateTime last || ScrolllerCacheBook.IsExpiredAt(last, nowUtc)
           || ScrolllerCacheBook.IsExpiredAt(last, nowUtc + ScrolllerDiskCache.SweepPeriod);

    /// <summary>"C:\...\browser_data_fyp\" -> "browser_data_fyp".</summary>
    internal static string ProfileKey(string? userDataFolder)
        => string.IsNullOrWhiteSpace(userDataFolder) ? "" : Path.GetFileName(userDataFolder.TrimEnd('\\', '/'));

    /// <summary>Every profile's disk cache cleared now, whatever its stamp (the off switch).
    /// Returns how many profiles were cleared; never throws.</summary>
    public static Task<int> ClearChromiumCachesAsync(string reason) => ClearAsync(reason, force: true);

    /// <summary>The hourly pass: only profiles whose stamp is due.</summary>
    internal static Task<int> ClearDueChromiumCachesAsync() => ClearAsync("day limit", force: false);

    private static void TrackForCacheClears(CoreWebView2 core)
    {
        try
        {
            var folder = core.Environment.UserDataFolder;
            var live = new LiveCore
            {
                Core = new WeakReference<CoreWebView2>(core), Dispatcher = Dispatcher.CurrentDispatcher,
                Profile = ProfileKey(folder), Folder = folder,
            };
            lock (ClearsLock)
            {
                Live.RemoveAll(l => !l.Core.TryGetTarget(out _));
                Live.Add(live);
            }
            ReadClears().TryGetValue(live.Profile, out var stamp);
            if (ChromiumClearDue(stamp.At, ClearsUtcNow())) _ = ClearOneAsync(live, "due at attach");
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerWebGate: cache-clear tracking failed: {E}", ex.Message); }
    }

    private static async Task<int> ClearAsync(string reason, bool force)
    {
        int cleared = 0;
        try
        {
            List<LiveCore> live;
            lock (ClearsLock)
            {
                Live.RemoveAll(l => !l.Core.TryGetTarget(out _));
                live = Live.ToList();
            }
            var stamps = ReadClears();
            var now = ClearsUtcNow();
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in live)
            {
                if (done.Contains(l.Profile)) continue;   // the cache is the profile's: once is enough
                stamps.TryGetValue(l.Profile, out var stamp);
                if (!force && !ChromiumClearDue(stamp.At, now)) { done.Add(l.Profile); continue; }
                if (await ClearOneAsync(l, reason).ConfigureAwait(false)) { done.Add(l.Profile); cleared++; }
            }
            // Profiles with no live core left: their windows are closed, so the folder goes on disk.
            // A profile still open is never touched on disk (its clear retries next hour).
            var open = new HashSet<string>(live.Select(l => l.Profile), StringComparer.OrdinalIgnoreCase);
            foreach (var (profile, stamp) in stamps)
            {
                if (open.Contains(profile) || (!force && !ChromiumClearDue(stamp.At, now))) continue;
                if (DeleteClosedProfileCache(stamp.Folder)) { WriteClear(profile, stamp.Folder, now); cleared++; }
            }
            // Profiles on disk with no stamp (filled by a build before the stamps, or never opened
            // since): due, and closed when no live core uses them.
            foreach (var folder in KnownProfileFolders())
            {
                var profile = ProfileKey(folder);
                if (profile.Length == 0 || open.Contains(profile) || stamps.ContainsKey(profile)) continue;
                if (DeleteClosedProfileCache(folder)) { WriteClear(profile, folder, now); cleared++; }
            }
            if (cleared > 0) App.Logger?.Information("ScrolllerWebGate: cleared {Count} browser disk cache(s) ({Reason})", cleared, reason);
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerWebGate: cache clear failed: {E}", ex.Message); }
        return cleared;
    }

    /// <summary>Clears one live core's profile disk cache on its own thread. False when it is gone
    /// or refuses; only a core that is gone is forgotten (<see cref="ForgetAfterFailedClear"/>).</summary>
    private static async Task<bool> ClearOneAsync(LiveCore l, string reason)
    {
        try
        {
            if (!l.Core.TryGetTarget(out var core) || l.Dispatcher.HasShutdownStarted) { Forget(l); return false; }
            await l.Dispatcher.InvokeAsync(() => core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache))
                .Task.Unwrap().WaitAsync(ClearBudget).ConfigureAwait(false);
            WriteClear(l.Profile, l.Folder, ClearsUtcNow());
            App.Logger?.Debug("ScrolllerWebGate: disk cache of {Profile} cleared ({Reason})", l.Profile, reason);
            return true;
        }
        catch (Exception ex)
        {
            bool alive = l.Core.TryGetTarget(out _) && !l.Dispatcher.HasShutdownStarted;
            if (ForgetAfterFailedClear(ex, alive)) Forget(l);
            App.Logger?.Debug("ScrolllerWebGate: disk cache clear of {Profile} failed: {E}", l.Profile, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// After a failed clear, is the core gone (forget it) or only slow (keep it)? Pure. A clear
    /// that ran past <see cref="ClearBudget"/> on a live core (a busy UI thread, a big cache) is
    /// kept: forgetting it made the next pass treat its profile as CLOSED, so the API clear was
    /// never tried again while the window stayed open, and the on-disk delete ran against a
    /// browser that still held the cache files (refused, so Chromium's copies outlived the day).
    /// </summary>
    internal static bool ForgetAfterFailedClear(Exception ex, bool coreAlive)
        => !coreAlive || ex is not TimeoutException;

    private static void Forget(LiveCore l)
    {
        lock (ClearsLock) Live.Remove(l);
    }

    /// <summary>Deletes <c>{folder}/EBWebView/*/Cache</c> of a profile nobody has open. True when
    /// nothing is left of it (or there was none).</summary>
    internal static bool DeleteClosedProfileCache(string? folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) return false;
            var web = Path.Combine(folder, "EBWebView");
            if (!Directory.Exists(web)) return true;
            bool whole = true;
            foreach (var profile in Directory.GetDirectories(web))
            {
                var cache = Path.Combine(profile, "Cache");
                if (!Directory.Exists(cache)) continue;
                try { Directory.Delete(cache, recursive: true); }
                catch { whole = false; }   // a browser process still holds it: the next sweep
            }
            return whole;
        }
        catch { return false; }
    }

    // ---- stamps -------------------------------------------------------------------------

    internal static string FormatClears(IReadOnlyDictionary<string, ClearStamp> stamps)
    {
        var o = new JObject();
        foreach (var (profile, s) in stamps)
            o[profile] = new JObject { ["t"] = s.At?.Ticks ?? 0, ["dir"] = s.Folder };
        return new JObject { ["v"] = 1, ["profiles"] = o }.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>Stamp text -> stamps. Bad text = none (every profile due).</summary>
    internal static Dictionary<string, ClearStamp> ParseClears(string? json)
    {
        var all = new Dictionary<string, ClearStamp>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return all;
        try
        {
            if (JObject.Parse(json)["profiles"] is not JObject o) return all;
            foreach (var p in o.Properties())
            {
                if (p.Value is not JObject row || string.IsNullOrWhiteSpace(p.Name)) continue;
                var ticks = (long?)row["t"] ?? 0;
                DateTime? at = ticks > 0 && ticks < DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
                all[p.Name] = new ClearStamp(at, (string?)row["dir"] ?? "");
            }
        }
        catch { all.Clear(); }
        return all;
    }

    private static Dictionary<string, ClearStamp> ReadClears()
    {
        lock (ClearsLock) return ReadClearsLocked();
    }

    private static Dictionary<string, ClearStamp> ReadClearsLocked()
    {
        try
        {
            var path = ClearsPathProvider();
            return ParseClears(path != null && File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch { return ParseClears(null); }
    }

    private static void WriteClear(string profile, string folder, DateTime at)
    {
        if (string.IsNullOrEmpty(profile)) return;
        try
        {
            lock (ClearsLock)
            {
                var path = ClearsPathProvider();
                if (path == null) return;
                var all = ReadClearsLocked();
                all[profile] = new ClearStamp(at, folder);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, FormatClears(all));
                File.Move(tmp, path, overwrite: true);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerWebGate: clear stamp write failed: {E}", ex.Message); }
    }
}
