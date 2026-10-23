using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The upgrade from WPF: every WebView2 profile the WPF head left under the user data folder (browser_data_fyp,
/// browser_data_video, arcademy, backroom ...) may still hold Chromium's disk-cached Scrolller clips from before this
/// head (WPF ScrolllerWebGate.CacheClears: such a profile with no live core has its <c>EBWebView/*/Cache</c> deleted
/// on disk). This head never opens those profiles, so each one's cache goes at the first sweep after the upgrade; the
/// sweep runs hourly and on the off switch, and a folder already empty costs one directory listing. A cache a running
/// browser still holds is refused and retried next hour. Only the Cache folder goes: a page's localStorage and
/// IndexedDB stay. A Linux profile has no EBWebView folder, so this is a no-op there.
/// </summary>
internal static class LegacyWebViewCaches
{
    /// <summary>Folders whose children are WebView2 user data folders. Replaceable by tests.</summary>
    internal static Func<IEnumerable<string>> RootsProvider = () => new[] { CorePaths.UserData };

    /// <summary>Deletes the disk cache of every WebView2 profile under the roots. Returns how many caches went.
    /// Never throws.</summary>
    internal static int SweepAll()
    {
        int cleared = 0;
        try
        {
            foreach (var root in RootsProvider())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                    cleared += DeleteProfileCaches(dir);
            }
        }
        catch (Exception ex) { Log.Debug("[scrolller sweep] WebView2 profile sweep failed: {E}", ex.Message); }
        if (cleared > 0) Log.Information("[scrolller sweep] {N} old WebView2 disk cache(s) deleted", cleared);
        return cleared;
    }

    /// <summary><c>{folder}/EBWebView/*/Cache</c> of one profile. Returns how many were deleted.</summary>
    internal static int DeleteProfileCaches(string folder)
    {
        int n = 0;
        try
        {
            var web = Path.Combine(folder, "EBWebView");
            if (!Directory.Exists(web)) return 0;
            foreach (var profile in Directory.GetDirectories(web))
            {
                var cache = Path.Combine(profile, "Cache");
                if (!Directory.Exists(cache)) continue;
                try { Directory.Delete(cache, recursive: true); n++; }
                catch { /* a browser process still holds it: the next sweep */ }
            }
        }
        catch { }
        return n;
    }
}
