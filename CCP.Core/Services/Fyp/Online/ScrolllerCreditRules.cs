using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// The "via Scrolller" credit rules every head shares (owner, 2026-10-16/17: Scrolller media on
/// screen is credited). WPF <c>ScrolllerCredit</c> forwards here; the marks themselves live in each head.
/// </summary>
internal static class ScrolllerCreditRules
{
    /// <summary>The brand mark every native surface shows (brief contract: never a loc key).</summary>
    public const string Mark = "via Scrolller";

    /// <summary>Wallpaper card credit follows the same online admission rule as WPF's pool.</summary>
    internal static bool WallpaperMediaEnabled(string? source, bool consent)
        => source != null && source != "local" && consent;

    /// <summary>The scrolller.com home page, for the calm credit lines (pickers, settings).</summary>
    public const string HomeLink = "https://scrolller.com";

    private static readonly Regex SubName = new("^[A-Za-z0-9_]{2,32}$", RegexOptions.CultureInvariant);

    /// <summary>https://scrolller.com/r/&lt;sub&gt;, or null for anything that is not a plain
    /// subreddit name (the link reaches a command line in a browser launcher's fallbacks).</summary>
    public static string? LinkFor(string? sub)
        => sub != null && SubName.IsMatch(sub) ? "https://scrolller.com/r/" + sub : null;

    /// <summary>The sub of an entry id shaped "scrolller/&lt;sub&gt;/&lt;postId&gt;", or null.</summary>
    public static string? SubFromId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var parts = id.Split('/');
        if (parts.Length != 3 || !string.Equals(parts[0], "scrolller", StringComparison.Ordinal)) return null;
        return LinkFor(parts[1]) == null ? null : parts[1];
    }

    /// <summary>Is the thing a surface is about to show Scrolller's? A scrolller url, an entry
    /// id shaped "scrolller/&lt;sub&gt;/&lt;postId&gt;", or a remote-media temp copy. A local file,
    /// a pack temp file and anything else: false.</summary>
    public static bool IsScrolllerMedia(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return false;
        if (ScrolllerPolicy.IsScrolllerUrl(pathOrUrl)) return true;
        if (SubFromId(pathOrUrl) != null) return true;
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            return Path.GetFileName(pathOrUrl).StartsWith(ScrolllerDiskCache.TempPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>The link may open only outside every lock: a browser coming up over Strict Lock,
    /// Lockdown or a Chaos run would be a way out of them.</summary>
    public static bool CanOpenLink(bool strict, bool lockdown, bool chaos) => !strict && !lockdown && !chaos;
}
