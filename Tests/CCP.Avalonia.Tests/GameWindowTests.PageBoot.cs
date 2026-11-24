using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF ChaosWebViewHost.cs:1700-1715 receives ready before calling the page-ready handler.
    private static readonly HashSet<string> EarlyBootGaps = new(StringComparer.Ordinal)
    { "breakout", "breakoutdemo", "fyp", "piecebypiece", "loom", "raceclassic", "dtrh", "prizecounter" };
    private static readonly HashSet<string> CarrierGaps = new(StringComparer.Ordinal)
    { "race", "glassroad" };

    public static IEnumerable<object[]> HostedGames => GameWindow.Games
        .Where(pair => !string.IsNullOrEmpty(pair.Value.Page))
        .Select(pair => new object[] { pair.Key });

    private static void BootLikeEngine(GameWindow window, params string[] frames)
    {
        window.Web.RecordAllowedNavigation(window.PageUrl!);
        foreach (var frame in frames) window.Web.OnWebMessage(frame);
        window.Web.OnNavigationCompleted(window.PageUrl);
    }

    private static void RunPageBoot(Action action)
    {
        var oldProvider = CoreSettings.ServiceProvider;
        try { Run((_, _) => action()); }
        finally { CoreSettings.ServiceProvider = oldProvider; }
    }

    [Theory]
    [MemberData(nameof(HostedGames))]
    public void EveryHostedGame_EarlyReadyBoots(string id) => RunPageBoot(() =>
    {
        var spec = GameWindow.Games[id];
        // Glassroad runs in Electron on Linux, not this WebHost.
        if (spec.Id == "glassroad" && OperatingSystem.IsLinux()) Assert.Skip("Glassroad uses Electron on Linux");
        var w = new GameWindow(spec);
        try
        {
            w.Load(GameWindow.Server());
            var sent = new List<string>();
            w.Posted += sent.Add;
            BootLikeEngine(w, "{\"type\":\"ready\"}");
            var knownGap = EarlyBootGaps.Contains(id);
            var handshakeSucceeded = w.IsReady && sent.Count == 1 && (string?)JObject.Parse(sent[0])["type"] == "init";
            Assert.False(knownGap && handshakeSucceeded, $"{id}: handshake now works; remove the tracked gap");
            Assert.SkipWhen(knownGap, $"{id}: tracked early-ready gap (ready={w.IsReady}, frames={sent.Count})");
            Assert.True(w.IsReady, $"{id}: early ready did not mark the host ready");
            Assert.Equal("init", (string?)JObject.Parse(Assert.Single(sent))["type"]);
        }
        finally { w.Close(); }
    });

    [Theory]
    [MemberData(nameof(HostedGames))]
    public void EveryHostedGame_PageCanFindItsHost(string id) => RunPageBoot(() =>
    {
        var spec = GameWindow.Games[id];
        var folder = Path.Combine(SourceRoots.RepoRoot, "Assets", "web", Path.GetDirectoryName(spec.Page)!);
        var scripts = Directory.GetFiles(folder, "*.js", SearchOption.AllDirectories)
            .Where(path => !path.Contains("test", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText).ToArray();
        Assert.NotEmpty(scripts);
        var needsCarrier = scripts.Any(s => s.Contains("chrome.webview"))
            && !scripts.Any(s => s.Contains("shared/carrier.js") || s.Contains("invokeCSharpAction"));
        var w = new GameWindow(spec);
        try
        {
            var passed = !needsCarrier || w.Web.InjectCarrier;
            Assert.True(CarrierGaps.Contains(id) != passed, $"{id}: needs carrier={needsCarrier}, injected={w.Web.InjectCarrier}; update the ratchet");
        }
        finally { w.Close(); }
    });
}
