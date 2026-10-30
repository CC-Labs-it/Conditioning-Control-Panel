using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// The For You feed's head-free rules, lifted out of WPF Services/Fyp/FypHostService.cs so both heads run
/// one copy. The window, frame dispatch, ghost mode and eye control stay in each head.
/// BRIGHT LINE: no CC Labs server is ever involved, and no media is fetched unless <see cref="RemoteAllowed"/>.
/// </summary>
internal static class FypRules
{
    /// <summary>WPF FypHostService.cs:1117 OnEyeGaze: calibration-monitor DIPs -> physical desktop -> feed client.
    /// Unknown monitor origins and invalid calibration scales retain the legacy fallback.</summary>
    public static (double X, double Y, bool Inside) GazePoint(double x, double y,
        double calibrationScale, double monitorX, double monitorY, bool knownMonitor,
        double webX, double webY, double webScale, double width, double height)
    {
        var dpi = calibrationScale is > 0.25 and < 8 ? calibrationScale : 1;
        var nx = (x * dpi + (knownMonitor ? monitorX : 0) - webX) / webScale / width;
        var ny = (y * dpi + (knownMonitor ? monitorY : 0) - webY) / webScale / height;
        return (Math.Clamp(nx, 0, 1), Math.Clamp(ny, 0, 1), nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1);
    }

    // Passive-XP cap: a misbehaving page can't fountain XP (12/min beats any real 20-40s slice rate).
    public const int MaxClipXpPerMinute = 12;
    public const int ClipXp = 5;
    public const int AttentionXp = 15;

    public static bool IsRemoteId(string? id) =>
        id != null && id.StartsWith("scrolller/", StringComparison.Ordinal);

    /// <summary>"library", "online" or "mixed": the feed's own picker once it left "library", else the
    /// app-wide source (Assets tab) behind the same consent every other remote surface reads.</summary>
    public static string EffectiveFeedSource(AppSettings? s)
    {
        if (s == null) return "library";
        if (s.FypSource != "library") return s.FypSource;
        if (s.MediaSource != "local" && s.HasRemoteMediaConsent) return s.MediaSource;
        return "library";
    }

    /// <summary>The mixed-mode remote share that goes with <see cref="EffectiveFeedSource"/>.</summary>
    public static int EffectiveOnlineRatio(AppSettings? s)
    {
        if (s == null) return 30;
        return s.FypSource != "library" ? s.FypOnlineRatio : s.RemoteMediaRatio;
    }

    /// <summary>May the feed fetch remote media right now? Consent AND a non-library source.</summary>
    public static bool RemoteAllowed(AppSettings? s) =>
        s != null && s.HasRemoteMediaConsent && EffectiveFeedSource(s) != "library";

    private static readonly Queue<DateTime> _clipXpTimes = new();

    /// <summary>At most <see cref="MaxClipXpPerMinute"/> paid clips in any rolling minute.</summary>
    public static bool AllowClipXp() => AllowClipXp(DateTime.UtcNow);

    internal static bool AllowClipXp(DateTime nowUtc)
    {
        lock (_clipXpTimes)
        {
            while (_clipXpTimes.Count > 0 && (nowUtc - _clipXpTimes.Peek()).TotalSeconds > 60)
                _clipXpTimes.Dequeue();
            if (_clipXpTimes.Count >= MaxClipXpPerMinute) return false;
            _clipXpTimes.Enqueue(nowUtc);
            return true;
        }
    }

    internal static void ResetClipXpForTests() { lock (_clipXpTimes) _clipXpTimes.Clear(); }

    /// <summary>The init payload on page-ready. <paramref name="eyeControl"/> false (a head with no camera
    /// path) keeps the page from painting a toggle "on" that nothing honours.</summary>
    public static JObject BuildInit(AppSettings? s, IEnumerable<FypAssetManifest.Entry> assets, bool eyeControl, string statsPath) =>
        JObject.FromObject(new
        {
            type = "init",
            assets = assets.ToList(),
            settings = new
            {
                layout = s?.FypLayout ?? "duo",
                includeGifs = s?.FypIncludeGifs ?? true,
                mosaicAutoChange = s?.FypMosaicAutoChange ?? true,
                mosaicChangeSec = s?.FypMosaicChangeSec ?? 10,
                autoAdvance = s?.FypAutoAdvance ?? false,
                muted = s?.FypMuted ?? false,
                volume = s?.FypVolume ?? 100,
                windowOpacity = s?.FypWindowOpacity ?? 0.6,
                audioGlow = s?.FypAudioGlow ?? true,
                eyeControl = eyeControl && (s?.FypEyeControl ?? false),
                eyeGaze = eyeControl && (s?.FypEyeGaze ?? false),
                // Effective, not raw: the app-wide media source reaches the feed too.
                source = EffectiveFeedSource(s),
                onlineRatio = EffectiveOnlineRatio(s),
                // Either consent card (the feed's or the app-wide one) counts.
                onlineConsented = s?.HasRemoteMediaConsent ?? false,
            },
            online = new
            {
                niches = FypOnlineCoordinator.Catalog.Select(n => new
                {
                    id = n.Id,
                    label = n.Label,
                    subs = n.Subs,
                    selected = s?.FypOnlineNiches?.Contains(n.Id) ?? false,
                }),
                customSubs = (s?.FypOnlineCustomSubs ?? new List<string>()).Select(name =>
                {
                    RemoteSubVerdict? v = null;
                    s?.FypOnlineSubVerdicts.TryGetValue(name, out v);
                    return new { name, ok = (bool?)v?.Ok, videoCount = v?.VideoCount };
                }),
                library = BuildLibraryPayload(s),
            },
            stats = LoadStats(statsPath),
        });

    /// <summary>What the head still has to do after <see cref="ApplySetting"/> stored a value.</summary>
    public enum SettingEffect
    {
        None,
        GhostOn, GhostOff,   // "clickThrough": session only, never stored
        EyeControlOn, EyeControlOff, EyeGazeChanged,
        OpacityChanged,      // stored; the ghost mirror (if any) follows
    }

    /// <summary>One <c>settings-changed</c> frame, never trusting the page: unknown keys ignored, no remote
    /// source without consent, catalog niches only, subs sanitized and capped at 20, consent one-way.</summary>
    public static SettingEffect ApplySetting(AppSettings? s, string? key, JToken? value)
    {
        if (key == null || value == null) return SettingEffect.None;
        // Session-only and settings-free ("clickThrough" is the wire name for ghost mode).
        if (key == "clickThrough")
        {
            bool on = false;
            try { on = (bool?)value ?? false; } catch { }
            return on ? SettingEffect.GhostOn : SettingEffect.GhostOff;
        }
        if (s == null) return SettingEffect.None;
        try
        {
            switch (key)
            {
                case "layout": s.FypLayout = (string?)value ?? "duo"; break;
                case "includeGifs": s.FypIncludeGifs = (bool?)value ?? true; break;
                case "mosaicAutoChange": s.FypMosaicAutoChange = (bool?)value ?? true; break;
                case "mosaicChangeSec": s.FypMosaicChangeSec = (int?)value ?? 10; break;
                case "autoAdvance": s.FypAutoAdvance = (bool?)value ?? false; break;
                case "muted": s.FypMuted = (bool?)value ?? false; break;
                case "volume": s.FypVolume = (int?)value ?? 100; break;   // clamped again by the property
                case "windowOpacity":
                    s.FypWindowOpacity = (double?)value ?? 1.0;
                    return SettingEffect.OpacityChanged;
                case "audioGlow": s.FypAudioGlow = (bool?)value ?? true; break;   // page-side visual: persist only
                case "eyeControl":
                {
                    bool on = (bool?)value ?? false;
                    s.FypEyeControl = on;
                    return on ? SettingEffect.EyeControlOn : SettingEffect.EyeControlOff;
                }
                case "eyeGaze":
                    s.FypEyeGaze = (bool?)value ?? false;
                    return SettingEffect.EyeGazeChanged;
                case "source":
                {
                    var src = (string?)value ?? "library";
                    // The page shows the consent card first; belt and braces for a stale page.
                    if (src != "library" && !s.HasRemoteMediaConsent) src = "library";
                    s.FypSource = src;
                    break;
                }
                case "onlineRatio": s.FypOnlineRatio = (int?)value ?? 30; break;
                case "onlineConsented":
                    // One-way: the page only ever sends true (accepting the card).
                    if ((bool?)value == true && !s.FypOnlineConsented)
                    {
                        s.FypOnlineConsented = true;
                        Log.Information("FypHost: online-content consent accepted");
                    }
                    break;
                case "onlineNiches":
                    if (value is JArray niches)
                    {
                        var known = new HashSet<string>(FypOnlineCoordinator.Catalog.Select(n => n.Id));
                        s.FypOnlineNiches = niches.Select(t => (string?)t)
                            .Where(id => id != null && known.Contains(id)).Select(id => id!)
                            .Distinct().ToList();
                        FypOnlineCoordinator.Fyp.ResetChannels();
                    }
                    break;
                case "onlineCustomSubs":
                    if (value is JArray subs)
                    {
                        s.FypOnlineCustomSubs = subs.Select(t => FypOnlineCoordinator.SanitizeSub((string?)t))
                            .Where(x => x != null).Select(x => x!)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
                        // The page's DESELECT path: drop verdicts neither the selection nor the LIBRARY keeps.
                        foreach (var name in s.FypOnlineCustomSubs) s.TryAddLibrarySub(name);
                        var kept = new HashSet<string>(s.FypOnlineCustomSubs, StringComparer.OrdinalIgnoreCase);
                        foreach (var name in s.LibrarySubs) kept.Add(name);
                        foreach (var gone in s.FypOnlineSubVerdicts.Keys.Where(k => !kept.Contains(k)).ToList())
                            s.FypOnlineSubVerdicts.Remove(gone);
                        FypOnlineCoordinator.Fyp.ResetChannels();
                    }
                    break;
            }
        }
        catch (Exception ex) { Log.Debug("FypHost: settings-changed {Key} failed: {E}", key, ex.Message); }
        return SettingEffect.None;
    }

    /// <summary>The library joined with verdicts and with this surface's selection.</summary>
    public static object[] BuildLibraryPayload(AppSettings? s)
    {
        if (s == null) return Array.Empty<object>();
        var rows = s.BuildRemoteSubLibraryView();
        var payload = new List<object>(rows.Count);
        foreach (var r in rows)
            payload.Add(new { name = r.Name, ok = r.Ok, videoCount = r.VideoCount, stillOnly = r.StillOnly, selected = r.Selected });
        return payload.ToArray();
    }

    /// <summary>The whole library after any change. Replace, never patch.</summary>
    public static JObject LibraryFrame(AppSettings? s) =>
        JObject.FromObject(new { type = "library", library = BuildLibraryPayload(s) });

    /// <summary>The X on a pill: the entry, its verdict and its feed membership go together, then every
    /// consumer's rotation resets (the name may have been a live channel for all of them).</summary>
    public static bool RemoveLibrarySub(AppSettings? s, string? rawName)
    {
        try
        {
            if (s == null || string.IsNullOrWhiteSpace(rawName)) return false;
            if (!s.RemoveLibrarySub(rawName)) return false;
            CoreSettings.Save();
            FypOnlineCoordinator.ResetAllChannels();
            Log.Information("[FYP online] r/{Sub} removed from the library", rawName.Trim());
            return true;
        }
        catch (Exception ex) { Log.Warning("FypHost: library remove failed: {E}", ex.Message); return false; }
    }

    /// <summary>A finished sub probe -> the sub-probe frame. A transport failure commits nothing; a real answer
    /// is remembered, and a found sub is kept in the library and added to the feed (20 at most).</summary>
    public static JObject CommitProbe(AppSettings? s, string clean, SubProbe probe)
    {
        if (s != null && probe.Error == null)
        {
            s.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict
            {
                Ok = probe.Ok,
                VideoCount = probe.VideoCount,
                CheckedAtUtc = DateTime.UtcNow,
            };
            if (probe.Ok)
            {
                s.TryAddLibrarySub(clean);
                var subs = s.FypOnlineCustomSubs.ToList();
                if (!subs.Contains(clean, StringComparer.OrdinalIgnoreCase) && subs.Count < 20)
                {
                    subs.Add(clean);
                    s.FypOnlineCustomSubs = subs;
                    // The sub list is app-wide (flashes, videos, intake, DTRH all resolve from it).
                    FypOnlineCoordinator.ResetAllChannels();
                    Log.Information("[FYP online] custom sub r/{Sub} verified ({N} videos) and added", clean, probe.VideoCount);
                }
            }
            CoreSettings.Save();
        }
        return JObject.FromObject(new { type = "sub-probe", sub = clean, ok = probe.Ok, videoCount = probe.VideoCount, error = probe.Error });
    }

    /// <summary>The frame for a name that is not a subreddit at all: the raw text is echoed back so the
    /// page can attach the error to the box the user is still looking at.</summary>
    public static JObject InvalidProbeFrame(string? rawSub) =>
        JObject.FromObject(new { type = "sub-probe", sub = rawSub ?? "", ok = false, videoCount = (int?)null, error = "invalid" });

    /// <summary>The frames after a remote batch: the entries (when any), then the status either way so the
    /// page clears its spinner. "added" is the FRESH count: the raw page kept the dry backoff from arming.
    /// <paramref name="newToPage"/>: ids this page was never handed (WPF ScrolllerReuse.CountNewToPage); a
    /// spent/away batch of cached files the page already holds is not fresh. Null: every entry is.</summary>
    public static IEnumerable<JObject> BatchFrames(FypOnlineCoordinator.FeedBatch batch, int? newToPage = null)
    {
        int count = batch.Entries?.Count ?? 0;
        int fresh = newToPage ?? count;
        if (count > 0) yield return JObject.FromObject(new { type = "assets-append", assets = batch.Entries });
        yield return JObject.FromObject(new
        {
            type = "online-status",
            ok = batch.Error == null,
            error = batch.Error,
            added = fresh,
            fresh = fresh,
            dry = batch.Dry,
            poolTotal = batch.PoolTotal,
        });
    }

    public static JObject? LoadStats(string path)
    {
        try
        {
            if (File.Exists(path)) return JObject.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) { Log.Warning("FypHost: stats load failed: {E}", ex.Message); }
        return null;
    }

    /// <summary>The page owns the stats shape; the blob is persisted verbatim.</summary>
    public static void SaveStats(string path, JObject stats)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, stats.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { Log.Warning("FypHost: stats save failed: {E}", ex.Message); }
    }
}
