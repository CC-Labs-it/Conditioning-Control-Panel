using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF ChaosWebViewHost.cs:1700-1715 receives ready before calling the page-ready handler.
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
            if (id == "piecebypiece") w.PbpOsAnimations = () => Task.FromResult(true);
            w.Load(GameWindow.Server());
            var sent = new List<string>();
            w.Posted += sent.Add;
            BootLikeEngine(w, "{\"type\":\"ready\"}");
            Assert.True(w.IsReady, $"{id}: early ready did not mark the host ready");
            // WPF LoomHostService.cs:140 sends loom-list; PieceByPieceHostService.cs:286 sends settings.
            var expected = id == "loom" ? "loom-list" : id == "piecebypiece" ? "pbp:settings" : "init";
            Assert.NotEmpty(sent);
            Assert.Equal(expected, (string?)JObject.Parse(sent[0])["type"]);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void EarlyReady_RequiresSuccessfulMatchingNavigation() => RunPageBoot(() =>
    {
        var w = new GameWindow(GameWindow.Games["breakoutdemo"]);
        try
        {
            w.Load(GameWindow.Server());
            var sent = new List<string>();
            w.Posted += sent.Add;
            var page = w.PageUrl!;
            var wrong = new Uri(page, "/other.html");
            void Start(Uri url)
            {
                var e = new WebViewNavigationStartingEventArgs { Request = url };
                w.Web.OnNavigationStarted(null, e);
                Assert.False(e.Cancel);
            }
            w.Web.OnWebMessage("{\"type\":\"ready\"}"); // no accepted navigation
            Start(page);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            Start(wrong); // a new start discards the pending ready
            w.Web.OnNavigationCompleted(wrong);
            Assert.Empty(sent);
            Start(page);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.Web.OnNavigationCompleted(page, success: false);
            Assert.Empty(sent);
            Start(page);
            w.Web.OnWebMessage("{\"type\":\"exit-done\"}");
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.Empty(sent);
            Assert.False(w.IsReady);
            w.Web.OnNavigationCompleted(page);
            Assert.True(w.IsReady);
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
