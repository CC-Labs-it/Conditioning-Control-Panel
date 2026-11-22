using System;
using System.IO;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// Host for Terms &amp; Conditioning (<c>Resources/web/fuzz/index.html</c>, wire <c>fuzz/CONTRACT.md</c> +
/// Contract v2). One tile for everyone: subscribers get the full game, free accounts the small one
/// (<see cref="TcRules"/>), so the tile is never locked. The safety doors are wired now (panic registry,
/// kept Escape); the window itself lands in the next slice. Until then, and on any build whose tree
/// has no game, the tile stays off the launcher (<see cref="TreePresent"/>).
/// </summary>
internal static class TcHostService
{
    /// <summary>Launcher id, panic registry id and shortcut icon name.</summary>
    public const string GameId = "tc";

    /// <summary>The page's kept-Escape frame (fuzz/host-escape.js HOST_ESCAPE).</summary>
    internal const string KeptEscapeType = "kept-escape";

    /// <summary>The game tree is not on main yet: the tile shows only when its page is on disk.</summary>
    public static bool TreePresent
    {
        get
        {
            try { return File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "web", "fuzz", "index.html")); }
            catch (Exception ex) { Diag.Swallowed(ex); return false; }
        }
    }

    // The window (a windowed ChaosWebViewHost, own profile) is the next slice; every safety door
    // below already reads it, so wiring the window changes no caller.
    private static ChaosWebViewHost? _host;

    public static bool IsActive => _host != null;

    /// <summary>The window is in front: the panel lets the page keep a first Escape as its pause.</summary>
    public static bool IsInFront => _host?.IsForeground == true;

    public static bool IsReady => _host?.IsReady == true;

    /// <summary>Hand the page an Escape the panel kept as its pause (pause card, never leave).</summary>
    public static void PostKeptEscape()
    {
        try { _host?.Post(new { type = KeptEscapeType }); _host?.FocusWeb(); }
        catch (Exception ex) { App.Logger?.Debug("Tc: kept Escape post failed: {E}", ex.Message); }
    }

    /// <summary>Open the game. Not wired yet: logs and returns (the tile is hidden until the tree lands).</summary>
    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        App.Logger?.Information("TcHostService: launch asked, window not wired yet (tree present: {Tree})", TreePresent);
    }

    /// <summary>The one funnel for every exit (panic included). Idempotent.</summary>
    public static void Close()
    {
        var h = _host;
        _host = null;
        if (h == null) return;
        try { h.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        App.Logger?.Information("TcHostService: closed");
    }
}
