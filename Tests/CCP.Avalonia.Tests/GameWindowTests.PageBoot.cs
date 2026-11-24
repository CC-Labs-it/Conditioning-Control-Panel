using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
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
            w.Web.OnNavigationCompleted(page); // a failed load must not leave a ready to replay
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EarlyReady_SurvivesTheReplacedLoadsLateFailure(bool reload, bool readyBeforeFailure) => RunPageBoot(() =>
    {
        // WPF ChaosWebViewHost.cs:1700-1715 takes ready from the document that sent it.
        var w = new GameWindow(GameWindow.Games["breakoutdemo"]);
        try
        {
            w.Load(GameWindow.Server());
            var sent = new List<string>();
            w.Posted += sent.Add;
            var page = w.PageUrl!;
            var first = reload ? page : new Uri(page, "/other.html");
            void Start(Uri url)
            {
                var e = new WebViewNavigationStartingEventArgs { Request = url };
                w.Web.OnNavigationStarted(null, e);
                Assert.False(e.Cancel);
            }
            Start(first);
            Start(page);
            if (readyBeforeFailure) w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.Web.OnNavigationCompleted(first, success: false); // the replaced load ends late
            Assert.Empty(sent);
            Assert.Equal(page, w.Web.ActiveNavigation);
            Assert.Null(w.Web.CurrentUrl);
            if (!readyBeforeFailure) w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.Web.OnNavigationCompleted(page);
            Assert.True(w.IsReady);
            Assert.Equal("init", (string?)JObject.Parse(Assert.Single(sent))["type"]);
        }
        finally { w.Close(); }
    });

    // Comments and strings may name chrome.webview without using it; only code counts.
    private static readonly System.Text.RegularExpressions.Regex Comments =
        new(@"/\*[\s\S]*?\*/|(?<![:'""\\])//[^\n]*");
    private static readonly System.Text.RegularExpressions.Regex WebviewUse = new(@"chrome\??\.webview");

    [Theory]
    [MemberData(nameof(HostedGames))]
    public void EveryHostedGame_PageCanFindItsHost(string id) => RunPageBoot(() =>
    {
        var spec = GameWindow.Games[id];
        var folder = Path.Combine(SourceRoots.RepoRoot, "Assets", "web", Path.GetDirectoryName(spec.Page)!);
        var scripts = Directory.GetFiles(folder, "*.js", SearchOption.AllDirectories)
            .Where(path => !path.Contains("test", StringComparison.OrdinalIgnoreCase))
            .Select(path => Comments.Replace(File.ReadAllText(path), "")).ToArray();
        var stringCarrier = scripts.Any(s => s.Contains("shared/carrier.js") || s.Contains("invokeCSharpAction"));
        Assert.True(stringCarrier || scripts.Any(WebviewUse.IsMatch), $"{id}: the page names no host carrier");
        var w = new GameWindow(spec);
        try
        {
            // WebView2 supplies chrome.webview; WebKitGTK and WPE have only invokeCSharpAction, so a page
            // without the string carrier needs the shim installed before its scripts (WebHost.CarrierShim).
            Assert.True(stringCarrier || w.Web.InjectCarrier, $"{id}: speaks only chrome.webview and gets no shim");
            var installed = new List<string>();
            w.Web.NativeCarrierInvoker = script => { installed.Add(script); return Task.FromResult(true); };
            w.Web.NewScrolllerEngine()(true);
            if (w.Web.InjectCarrier) Assert.Equal(WebHost.CarrierShim, Assert.Single(installed));
            // The page's frame through that carrier reaches the host, and the host answers.
            if (id == "piecebypiece") w.PbpOsAnimations = () => Task.FromResult(true);
            w.Load(GameWindow.Server());
            var sent = new List<string>();
            w.Posted += sent.Add;
            w.Web.RecordAllowedNavigation(w.PageUrl!);
            w.Web.OnNavigationCompleted(w.PageUrl);
            sent.Clear();
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.True(w.IsReady, $"{id}: ready through the carrier did not reach the host");
            Assert.NotEmpty(sent);
        }
        finally { w.Close(); }
    });
}
