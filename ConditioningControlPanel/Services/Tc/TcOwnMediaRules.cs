using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// "Use local files" on desktop (fuzz/CONTRACT.md 3.3, the library): the page asks <c>own-media-list</c> once at start
/// and the host answers <c>own-media</c> with the pictures the app itself uses, served from <c>ccp.assets</c> like every
/// other host's local media. The sources are the ones Back Room's <c>local</c> deal and Just Drop read: the DISK half of
/// the Flash Images pool (<c>FlashService.SnapshotLocalImagePaths</c>, deselected files pruned) and the For You video
/// walk (deselections and failing files left out). Pack entries have no <c>ccp.assets</c> url and stay out, as in Back
/// Room. Identity is the WHOLE path: the row's <c>name</c> is the path under the assets root, so two folders holding the
/// same file name stay two files on the page. No I/O here: the host passes the file reads in.
/// </summary>
internal static class TcOwnMediaRules
{
    /// <summary>The one origin the rows come from (the <c>ccp.assets</c> mapping, Allow, so the game can read it).</summary>
    public const string MediaOrigin = TcHostRules.OwnMediaOrigin;

    /// <summary>The page keeps at most this many own files from one answer (ui/pictures.js OWN_MAX).</summary>
    public const int MaxRows = 3000;

    /// <summary>One candidate: a full path and whether it came from the video walk.</summary>
    public readonly record struct Source(string Path, bool Video);

    /// <summary>A file's size in bytes and last write in epoch milliseconds, or null when it is gone.</summary>
    public readonly record struct Stat(long Size, long MtimeMs);

    private static readonly Dictionary<string, (string Kind, string Mime)> Pictures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".gif"] = ("gif", "image/gif"),
        [".png"] = ("picture", "image/png"),
        [".jpg"] = ("picture", "image/jpeg"),
        [".jpeg"] = ("picture", "image/jpeg"),
        [".jpe"] = ("picture", "image/jpeg"),
        [".jfif"] = ("picture", "image/jpeg"),
        [".bmp"] = ("picture", "image/bmp"),
        [".avif"] = ("picture", "image/avif"),
        [".webp"] = ("picture", "image/webp"),   // animated ones read as gif (WebpAnimates)
    };

    private static readonly Dictionary<string, string> Clips = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".m4v"] = "video/mp4",
    };

    /// <summary><c>caps.ownMedia</c>: the library, no folder of its own (the folder row and its window stay the browser's).</summary>
    public static JObject Cap() => TcHostRules.OwnMediaCap();   // one shape, kept in Core (TcHostRules.InitFrame sends it)

    /// <summary>Whether the page can show this file at all, from its name (no header read).</summary>
    public static bool Showable(string path, bool video)
    {
        var ext = System.IO.Path.GetExtension(path ?? "");
        return video ? Clips.ContainsKey(ext) : Pictures.ContainsKey(ext);
    }

    /// <summary>The page's kind and the MIME type. A <c>.webp</c> is a gif only when <paramref name="webpAnimates"/> says
    /// its header carries the animation flag (the rule of ui/local-image-motion.js); null for anything not showable.</summary>
    public static (string Kind, string Mime)? KindFor(string path, bool video, Func<string, bool> webpAnimates)
    {
        var ext = System.IO.Path.GetExtension(path ?? "");
        if (video) return Clips.TryGetValue(ext, out var vm) ? ("clip", vm) : null;
        if (!Pictures.TryGetValue(ext, out var p)) return null;
        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) && webpAnimates(path!)) return ("gif", p.Mime);
        return p;
    }

    /// <summary>
    /// The <c>own-media</c> rows: showable files under <paramref name="assetsRoot"/> only (only those have a
    /// <c>ccp.assets</c> url), remote entries dropped, deduped on the whole path, sorted so the listing order cannot move
    /// the answer, and past <see cref="MaxRows"/> a random spread. Headers and sizes are read only for the rows kept.
    /// </summary>
    public static JArray Rows(IEnumerable<Source>? sources, string? assetsRoot, Func<string, bool> webpAnimates,
        Func<string, Stat?> stat, Random rng)
    {
        var rows = new JArray();
        if (sources == null || string.IsNullOrWhiteSpace(assetsRoot)) return rows;
        string rootFull;
        try { rootFull = System.IO.Path.GetFullPath(assetsRoot); } catch { return rows; }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pool = new List<(string Full, bool Video, string Url)>();
        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s.Path) || IsRemote(s.Path) || !Showable(s.Path, s.Video)) continue;
            string full;
            try { full = System.IO.Path.GetFullPath(s.Path); } catch { continue; }
            var url = BackRoom.BackRoomMedia.ToAssetsUrl(rootFull, full);
            if (url == null || !seen.Add(full)) continue;
            pool.Add((full, s.Video, url));
        }
        pool.Sort((a, b) =>
        {
            int c = StringComparer.OrdinalIgnoreCase.Compare(a.Full, b.Full);
            return c != 0 ? c : StringComparer.Ordinal.Compare(a.Full, b.Full);
        });
        if (pool.Count > MaxRows)
        {
            for (int i = 0; i < MaxRows; i++) { int j = i + rng.Next(pool.Count - i); (pool[i], pool[j]) = (pool[j], pool[i]); }
            pool.RemoveRange(MaxRows, pool.Count - MaxRows);
        }

        foreach (var (full, video, url) in pool)
        {
            var st = stat(full);
            if (st is not { Size: > 0 } info) continue;   // gone, or empty: the page drops a zero size anyway
            if (KindFor(full, video, webpAnimates) is not { } kind) continue;
            rows.Add(new JObject
            {
                ["url"] = url,
                ["name"] = System.IO.Path.GetRelativePath(rootFull, full).Replace('\\', '/'),
                ["type"] = kind.Mime,
                ["size"] = info.Size,
                ["mtime"] = info.MtimeMs,
                ["kind"] = kind.Kind,
            });
        }
        return rows;
    }

    /// <summary>The <c>own-media</c> answer to <c>own-media-list</c>: no path is shown for the library, and it is never
    /// the answer to a pick (<c>asked:false</c>), so nothing changes source until the player presses Use local files.</summary>
    public static JObject Frame(JArray rows)
        => new() { ["type"] = "own-media", ["v"] = 1, ["path"] = "", ["rows"] = rows, ["asked"] = false };

    private static bool IsRemote(string path)
        => path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
