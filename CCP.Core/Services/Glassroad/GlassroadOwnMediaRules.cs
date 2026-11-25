// Adapted from WPF GlassroadOwnMediaRules.cs:18; the host supplies its asset URL mapping.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad;

/// <summary>
/// "My files" on desktop (glassroad CONTRACT.md section 7, "Desktop library"): the page asks <c>own-media-list</c> once
/// per page load and the host answers <c>own-media</c> with the player's ACTIVE (ticked) own pictures and clips, served
/// from <c>ccp.assets</c> like every other host's local media. The source is the active media walk
/// (the same walk DtRH, chess and Goon read: ticks honoured, browser formats only, size caps), so unticked files and
/// content packs (no <c>ccp.assets</c> url) stay out. Identity is the WHOLE path: two folders holding the same file name
/// stay two rows. No I/O here: the host passes the walk in. Nothing here touches the course or the feel, so the online
/// course hash cannot move.
/// </summary>
internal static class GlassroadOwnMediaSelection
{
    /// <summary>The one origin the rows come from (the window's <c>ccp.assets</c> mapping).</summary>
    public const string MediaOrigin = "https://ccp.assets";

    /// <summary>At most this many rows per answer; the page samples 12 of them for each course
    /// (host/localLibrary.js MAX_LIBRARY_FILES), so every course gets a different mix.</summary>
    public const int MaxRows = 96;

    /// <summary>One candidate from the walk: a full path and whether it is a picture (else a clip).</summary>
    public readonly record struct Source(string Path, bool IsImage);

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private static readonly HashSet<string> Clips = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".webm", ".m4v" };

    /// <summary><c>caps.ownMedia</c>: the library and the origin its rows sit on.</summary>
    public static JObject Cap(string mediaOrigin = MediaOrigin) => new() { ["library"] = true, ["mediaOrigin"] = mediaOrigin };

    /// <summary>
    /// The <c>own-media</c> rows: showable files under <paramref name="assetsRoot"/> only (only those have a
    /// <c>ccp.assets</c> url), remote entries dropped, deduped on the whole path, sorted so the walk order cannot move
    /// the answer, and past <see cref="MaxRows"/> a random spread.
    /// </summary>
    public static JArray Rows(IEnumerable<Source>? sources, string? assetsRoot, Random rng, Func<string, string> assetUrl)
    {
        var rows = new JArray();
        if (sources == null || string.IsNullOrWhiteSpace(assetsRoot)) return rows;
        string rootFull;
        try { rootFull = Path.GetFullPath(assetsRoot); } catch { return rows; }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pool = new List<(string Full, string Rel, bool IsImage)>();
        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s.Path) || IsRemote(s.Path)) continue;
            var ext = Path.GetExtension(s.Path);
            if (!(s.IsImage ? Pictures : Clips).Contains(ext)) continue;
            string full, rel;
            try
            {
                full = Path.GetFullPath(s.Path);
                rel = Path.GetRelativePath(rootFull, full);
            }
            catch { continue; }
            if (rel == "." || rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(rel)) continue;
            if (!seen.Add(full)) continue;
            pool.Add((full, rel, s.IsImage));
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
        foreach (var (_, rel, isImage) in pool)
            rows.Add(new JObject { ["url"] = assetUrl(rel), ["kind"] = isImage ? "image" : "video" });
        return rows;
    }

    /// <summary>Host -&gt; page <c>own-media</c>, the answer to <c>own-media-list</c>.</summary>
    public static JObject Frame(JArray rows) => new() { ["type"] = "own-media", ["v"] = 1, ["rows"] = rows };

    private static bool IsRemote(string path)
        => path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
