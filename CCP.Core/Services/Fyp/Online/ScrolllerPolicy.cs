using System;
using System.Threading;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// What the server currently allows for Scrolller media (owner, 2026-10-18: the Scrolller licence
/// needs a way to turn every install off or lower the clip size without a release). One place
/// every surface asks: native fetches, the media cache and the WebView2 pages. Defaults are the
/// shipped behaviour (on, 720p) so a missing or failed config read never changes anything.
/// </summary>
internal static class ScrolllerPolicy
{
    public const int DefaultMaxClipShortSide = 720;

    private static int _enabled = 1;
    private static int _maxClipShortSide = DefaultMaxClipShortSide;

    /// <summary>False = no request to any scrolller.com host from this app, anywhere.</summary>
    public static bool Enabled => Volatile.Read(ref _enabled) == 1;

    /// <summary>Largest clip short side to request, in pixels. Never above the default.</summary>
    public static int MaxClipShortSide => Volatile.Read(ref _maxClipShortSide);

    private static int _away;

    /// <summary>True while nobody can be watching (screen locked, display asleep): no FRESH
    /// Scrolller fetch starts, cached media keeps playing (owner, 2026-10-19). Set by the away
    /// watcher; never true on its own.</summary>
    public static bool Away
    {
        get => Volatile.Read(ref _away) == 1;
        set => Volatile.Write(ref _away, value ? 1 : 0);
    }

    private static int _official;

    /// <summary>True when the server says so (<c>official: true</c>, the owner's switch; it may flip
    /// while the licence is still in the works, the copy says so): the
    /// Assets tab shows the one-time "official" note and the stamped credit line. False by
    /// default and on any answer that does not say an explicit true.</summary>
    public static bool Official
    {
        get => Volatile.Read(ref _official) == 1;
        set => Volatile.Write(ref _official, value ? 1 : 0);
    }

    /// <summary>True for scrolller.com or a subdomain over http(s) (WPF ScrolllerCredit.IsScrolllerUrl forwards here).</summary>
    internal static bool IsScrolllerUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        var host = uri.Host;
        return string.Equals(host, "scrolller.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".scrolller.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Applies the server's answer. A cap above the default is ignored (the server
    /// can only lower it); a cap under 240 falls back to 240.</summary>
    public static void Apply(bool enabled, int? maxClipShortSide)
    {
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
        var cap = maxClipShortSide ?? DefaultMaxClipShortSide;
        if (cap > DefaultMaxClipShortSide) cap = DefaultMaxClipShortSide;
        if (cap < 240) cap = 240;
        Volatile.Write(ref _maxClipShortSide, cap);
    }
}
