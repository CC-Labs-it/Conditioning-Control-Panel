using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>Identity needed by the on-picture credit (WPF ScrolllerCredit.IsScrolllerMedia).
/// Kept separate from rendering so every head uses the same attribution boundary.</summary>
internal static class ScrolllerCredit
{
    internal const string Mark = "via Scrolller";

    internal static bool IsScrolllerMedia(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return false;
        if (ScrolllerPolicy.IsScrolllerUrl(pathOrUrl)) return true;
        var parts = pathOrUrl.Split('/');
        if (parts.Length == 3 && parts[0] == "scrolller"
            && Regex.IsMatch(parts[1], "^[A-Za-z0-9_]{2,32}$", RegexOptions.CultureInvariant)) return true;
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        // ponytail: WPF's remote-temp naming contract; no remote flash fetcher is added in this lane.
        try { return Path.GetFileName(pathOrUrl).StartsWith("ccp_temp_remote_", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
}
