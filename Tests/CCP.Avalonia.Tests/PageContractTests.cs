using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Host-to-page contract (lessons #5489, #5782): a library url the Avalonia host sends must survive the
/// bundled page's own asset-url rule, run in node. Covers every page under Assets/web whose rule a module exports.</summary>
public class PageContractTests
{
    /// <summary>The two shapes the host sends: <see cref="WebAssetServer.AssetPath"/> and server.AssetUrl
    /// (that path behind the page's own origin, WebAssetServer.cs AssetUrl).</summary>
    static readonly Dictionary<string, string> Forms = new()
    {
        ["AssetPath"] = WebAssetServer.AssetPath("images/a b.png"),
        ["AssetUrl"] = PageContract.Origin + WebAssetServer.AssetPath("images/a b.png"),
    };

    /// <summary>Page -> (hosted page, module, JS call returning whether the page keeps the url as local media).</summary>
    static readonly Dictionary<string, (string Page, string Module, string Call)> Pages = new()
    {
        ["arcademy"] = ("arcademy/index.html", "arcademy/provider/inventory.js",
            "(m, u) => { const p = m.buildLocalPools([u], {}); return [...p.loop, ...p.still].includes(u) && m.isLocalUrl(u); }"),
        ["dtrh"] = ("dtrh/index.html", "dtrh/hostMedia.js",
            "(m, u) => { const s = m.createHostMediaSource(); s.setManifest({ images: [{ name: 'a b.png', url: u }] }); return s.hasUserMedia(); }"),
        ["glassroad"] = ("glassroad/index.html", "glassroad/render/mediaMath.js", "(m, u) => m.urlAllowed(u, location.origin)"),
        ["backroom-hypno"] = ("backroom/index.html", "backroom/shared/hypno/media.js", "(m, u) => m.drawableUrl(u)"),
        ["intake-records"] = ("intake/index.html", "intake/core/mediaLog.js",
            "(m, u) => { m.resetMediaLog(); m.noteMedia(u, 'gif'); return m.recordedMedia().some(r => r.url === u); }"),
    };

    // TODO(host-page-contract): rules no module exports, so node cannot drive them without a page refactor.
    // All of them accept the page's own origin (location.origin), which both forms resolve to.
    //   backroom/room/screens.js allowed(), backroom/stations/slot/media.js allowed(),
    //   backroom/stations/counter/station.js allowedUrl(), intake/render/captcha/grid.js isLocalMediaUrl(),
    //   first-show/index.html CSP img-src/media-src ('self' covers the 127.0.0.1 origin).

    /// <summary>Known rejections on this base. Each must still fail; a fixed one fails the test until it is removed here.</summary>
    static readonly Dictionary<(string Page, string Form), string> KnownRejects = new()
    {
        // toAssetUrl rewrites the root-relative path to https://ccp.assets/ccp.assets/... (fixed on avalonia-port/s10-arcademy-local-assets, 277a8d8f88).
        [("arcademy", "AssetPath")] = "toAssetUrl prefixes root-relative urls with ASSET_HOST",
        // isLocalUrl knows only https?://ccp.*/ hosts, so a 127.0.0.1 url is a tainted remote; the host should send AssetPath.
        [("arcademy", "AssetUrl")] = "isLocalUrl treats the 127.0.0.1 origin as remote",
        // LOCAL_URL_RE knows https?://ccp.*/ and root-relative only. GameWindow.Dtrh.cs sends AssetPath, but
        // GameWindow.Race.cs (dtrh/race, same hostMedia.js) sends server.AssetUrl: race library media lands in the remote, DOM-only pool.
        [("dtrh", "AssetUrl")] = "hostMedia LOCAL_URL_RE treats the 127.0.0.1 origin as remote",
        // Same LOCAL_URL_RE; IntakeHostWindow's AssetsBase is the AssetUrl form, so the Records Office keeps no local payloads.
        [("intake-records", "AssetUrl")] = "mediaLog LOCAL_URL_RE treats the 127.0.0.1 origin as remote",
    };

    public static TheoryData<string, string> Cases() =>
        new(Pages.Keys.SelectMany(page => Forms.Keys.Select(form => (page, form))));

    [Theory]
    [MemberData(nameof(Cases))]
    public void PageContract_PageKeepsTheAssetUrlTheHostSends(string page, string form)
    {
        var (html, module, call) = Pages[page];
        var url = Forms[form];
        var kept = (bool)PageContract.Run(html, module, call, url);
        if (KnownRejects.TryGetValue((page, form), out var why))
            Assert.False(kept, $"{page} now keeps the {form} url {url}; remove it from KnownRejects ({why}).");
        else
            Assert.True(kept, $"{page} ({module}) drops the {form} url {url} the host sends.");
    }
}
