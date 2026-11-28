using System;

namespace ConditioningControlPanel.Services.Browser;

// ccp-bugs #1693. BambiCloud's desktop layout (viewport wider than 1000 CSS px) is a grid whose
// header row is a fixed 13vh, with the menu centred in it. In the dashboard browser of a
// non-maximised window the viewport is wide enough for that layout but short, so 13vh is small,
// the menu wraps to two or three lines, and the centred block spills out of the row: the top
// line lands above the page and cannot be scrolled to. Full screen gives the row enough height.
// The fix lets the header row grow to fit its menu and takes the difference off the content row,
// which scrolls on its own. When the menu and the header's own padding fit in 13vh nothing moves
// (13vh + 77vh + 10vh); on a large screen where the menu already filled the row edge to edge, the
// header gains its padding back (measured headless: 130 -> 170 px at 1900x1000).
internal static class BambiCloudHeaderFit
{
    internal const string StyleId = "ccp-bambicloud-header-fit";

    // `.app` has height:100% of an auto-height #root (so effectively auto, rows summing to 100vh);
    // pinning it to 100vh keeps that total while the header row is allowed to grow. Scoped to the
    // grid layout only: at 1000 px and under the site uses a flex column that this must not touch.
    internal const string Css =
        "@media (width > 1000px){" +
        ".app:not(.app-hidden-player){height:100vh;grid-template-rows:minmax(13vh,max-content) minmax(0,1fr) 10vh}" +
        ".app.app-hidden-player{height:100vh;grid-template-rows:minmax(13vh,max-content) minmax(0,1fr)}" +
        "}";

    internal static bool AppliesTo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        var host = uri.Host.ToLowerInvariant();
        return host == "bambicloud.com" || host.EndsWith(".bambicloud.com", StringComparison.Ordinal);
    }

    // Idempotent: one style element per document, found again by id.
    internal static string Script =>
        "(function(){if(document.getElementById('" + StyleId + "'))return;" +
        "var s=document.createElement('style');s.id='" + StyleId + "';" +
        "s.textContent='" + Css + "';" +
        "(document.head||document.documentElement).appendChild(s);})();";
}
