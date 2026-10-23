using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Graded Intake slice 2: the page is served by WebAssetServer, the host answers its
/// "ready" with WPF's init, the media it names is served under the same token, and a finished run
/// reaches the Sessions list (WPF IntakeHostService.Launch / OnPageReady / OnQuizResult).</summary>
public sealed class IntakePageTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task TheServedPageGetsInitAndItsMediaFromTheSameOrigin()
    {
        var web = Directory.CreateTempSubdirectory("intake-web-").FullName;
        var assets = Directory.CreateTempSubdirectory("intake-assets-").FullName;
        Directory.CreateDirectory(Path.Combine(web, "intake"));
        File.WriteAllText(Path.Combine(web, "intake", "index.html"), "<html></html>");
        Directory.CreateDirectory(Path.Combine(assets, "images"));
        var gif = Path.Combine(assets, "images", "a b.gif");
        File.WriteAllText(gif, "GIF89a");
        var prevAssets = CorePaths.EffectiveAssetsProvider;
        CorePaths.EffectiveAssetsProvider = () => assets;
        using var server = new WebAssetServer(web) { AssetsRoot = () => assets };
        try
        {
            string gifUrl = "";
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                EnsureApp();
                var host = new IntakeHostWindow();
                try
                {
                    host.Load(server);
                    Assert.Equal(new Uri(server.Url("intake/index.html")), host.PageUrl);
                    Assert.Equal(1, host.Web.NavigationRequests);

                    // The window never leaves the served origin.
                    Assert.True(host.Web.AllowNavigation!(new Uri($"http://127.0.0.1:{server.Port}/intake/x.html")));
                    Assert.False(host.Web.AllowNavigation!(new Uri($"http://127.0.0.1:{server.Port + 1}/intake/index.html")));
                    Assert.False(host.Web.AllowNavigation!(new Uri("https://example.com/")));

                    var init = JObject.FromObject(host.InitMessage());
                    Assert.Equal("init", (string?)init["type"]);
                    Assert.Equal("default", (string?)init["config"]!["niche"]);   // no bank in this root
                    gifUrl = (string)init["config"]!["media"]!["gifs"]![0]!;
                    Assert.Equal($"http://127.0.0.1:{server.Port}/{WebAssetServer.AssetsPrefix}images/a%20b.gif", gifUrl);
                    // A sandbox never hands the page the real AI server.
                    Assert.True(SandboxNet.Active);
                    Assert.Equal(JTokenType.Null, init["ai"]!.Type);
                }
                finally { host.Close(); }
                return Task.CompletedTask;
            });

            Assert.Equal(gif, server.ResolveFile(new Uri(gifUrl).AbsolutePath));
            Assert.Null(server.ResolveFile("/" + WebAssetServer.AssetsPrefix + "../" + Path.GetFileName(web) + "/intake/index.html"));
            using var http = new HttpClient();
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(gifUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(gifUrl + "?ccp_t=" + server.Token)).StatusCode);
        }
        finally
        {
            CorePaths.EffectiveAssetsProvider = prevAssets;
            Directory.Delete(web, true);
            Directory.Delete(assets, true);
        }
    }

    [Fact]
    public async Task ADraftedSessionJoinsTheSessionsListAndCanBeRevealed()
    {
        var root = Directory.CreateTempSubdirectory("intake-drafted-").FullName;
        var custom = Path.Combine(root, "custom");
        var builtIn = Path.Combine(root, "built-in");
        Directory.CreateDirectory(custom);
        Directory.CreateDirectory(builtIn);
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                EnsureApp();
                var files = new SessionFileService(custom, builtIn);
                var manager = new SessionManager(files);
                manager.LoadAllSessions();
                var view = new PresetsTabView();
                view.UseSessionManager(manager);

                var session = Session.MorningDrift;
                session.Id = "intake-drafted";
                session.Name = "Drafted";
                var path = Path.Combine(custom, "drafted.session.json");
                Assert.Null(view.RevealSession(session.Id));

                view.RegisterExternallySavedSession(session, path);
                view.RegisterExternallySavedSession(session, path);   // a second register adds nothing
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Default);

                Assert.Single(manager.AllSessions, s => s.Id == "intake-drafted");
                Assert.Same(session, view.RevealSession("intake-drafted"));
                Assert.True(File.Exists(path));
                return Task.CompletedTask;
            });
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>WPF IntakeHostService.OnQuizResult tail: the draft queues half a punch on its session id,
    /// and the toast's "Run it now" opens Sessions with that session selected and asks to start it.</summary>
    [Fact]
    public async Task RunItNowQueuesThePunchAndAsksToStartTheDraftedSession()
    {
        var root = Directory.CreateTempSubdirectory("intake-run-now-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "custom"));
        Directory.CreateDirectory(Path.Combine(root, "built-in"));
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var card = AvApp.IntakePunchCard = new IntakePunchCard(Path.Combine(root, IntakePunchCardState.FileName));
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            var shell = new MainShellWindow();
            var host = shell.Named<Panel>("NotificationHost")!;
            AvApp.Notifications.AttachHost(host);
            shell.Show();
            try
            {
                var presets = shell.Named<PresetsTabView>("PresetsTab")!;
                var manager = new SessionManager(new SessionFileService(Path.Combine(root, "custom"), Path.Combine(root, "built-in")));
                manager.LoadAllSessions();
                presets.UseSessionManager(manager);
                var session = new Session { Id = "intake-run-now", Name = "Run Now Draft", DurationMinutes = 1, IsAvailable = true };
                Assert.NotEqual("presets", shell.CurrentTab);

                GradedIntakeTabView.OnSessionDrafted(shell, session, Path.Combine(root, "custom", "run-now.session.json"));
                Assert.Equal("intake-run-now", Assert.Single(card.State.PendingDrafts).SessionId);

                var action = host.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ToastAction");
                action.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
                Assert.Equal("presets", shell.CurrentTab);
                Assert.EndsWith("Start Run Now Draft?", ask.FindControl<TextBlock>("TxtTitle")!.Text);
                ask.FindControl<Button>("BtnCancel")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any() ? null : shell);
                Assert.False(runner.IsRunning);   // the normal confirmation: nothing starts by surprise
            }
            finally
            {
                foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
                shell.Close();
                runner.Stop();
                CoreSession.IsSessionRunningProvider = null;
                AvApp.Sessions = null;
                AvApp.IntakePunchCard = null;
            }
        });
        Directory.Delete(root, true);
    }

    private static async Task<T> WaitFor<T>(Func<T?> probe) where T : class
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } hit) return hit;
            await Task.Yield();
        }
        throw new TimeoutException("side effect never appeared");
    }

    /// <summary>ccp.assets/ is media from the user's library and nothing else: no other file types,
    /// no app dot-folders, no unchecked assets, no escape by .. or symlink, never the profile folder.</summary>
    [Fact]
    public void TheAssetsPrefixServesOnlyActiveLibraryMedia()
    {
        var web = Directory.CreateTempSubdirectory("intake-web-").FullName;
        var assets = Directory.CreateTempSubdirectory("intake-assets-").FullName;
        var outside = Directory.CreateTempSubdirectory("intake-outside-").FullName;
        var profileImage = Path.Combine(CorePaths.UserData, "images", "p.png");
        try
        {
            File.WriteAllText(Path.Combine(web, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(web, "w.png"), "x");
            Directory.CreateDirectory(Path.Combine(assets, "images"));
            Directory.CreateDirectory(Path.Combine(assets, ".temp"));
            foreach (var f in new[] { "images/a.gif", "images/off.png", "images/page.html", ".temp/t.png" })
                File.WriteAllText(Path.Combine(assets, f), "x");
            File.WriteAllText(Path.Combine(outside, "secret.png"), "x");
            File.CreateSymbolicLink(Path.Combine(assets, "images", "link.png"), Path.Combine(outside, "secret.png"));
            Directory.CreateSymbolicLink(Path.Combine(assets, "images", "out"), outside);

            using var server = new WebAssetServer(web) { AssetsRoot = () => assets, DisabledAssets = () => new[] { "images/off.png" } };
            string P(string rel) => "/" + WebAssetServer.AssetsPrefix + rel;
            Assert.Equal(Path.Combine(assets, "images", "a.gif"), server.ResolveFile(P("images/a.gif")));
            Assert.Null(server.ResolveFile(P("images/page.html")));     // not media
            Assert.Null(server.ResolveFile(P(".temp/t.png")));          // the app's own folder
            Assert.Null(server.ResolveFile(P("images/off.png")));       // unchecked in the Assets tree
            Assert.Null(server.ResolveFile(P("../settings.json")));
            Assert.Null(server.ResolveFile(P("../" + Path.GetFileName(web) + "/w.png")));
            Assert.Null(server.ResolveFile(P("images/link.png")));      // symlink escape
            Assert.Null(server.ResolveFile(P("images/out/secret.png")));

            Directory.CreateDirectory(Path.GetDirectoryName(profileImage)!);
            File.WriteAllText(profileImage, "x");
            using var profile = new WebAssetServer(web) { AssetsRoot = () => CorePaths.UserData, DisabledAssets = () => null };
            Assert.Null(profile.ResolveFile(P("images/p.png")));
            using var parent = new WebAssetServer(web) { AssetsRoot = () => Path.GetDirectoryName(CorePaths.UserData), DisabledAssets = () => null };
            Assert.Null(parent.ResolveFile(P(Path.GetFileName(CorePaths.UserData) + "/images/p.png")));
        }
        finally
        {
            File.Delete(profileImage);
            Directory.Delete(web, true);
            Directory.Delete(assets, true);
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task DownloadedAudioPlaysThroughTheAuthenticatedPageOrigin()
    {
        var web = Directory.CreateTempSubdirectory("intake-web-").FullName;
        var content = Directory.CreateTempSubdirectory("intake-content-").FullName;
        var outside = Directory.CreateTempSubdirectory("intake-outside-").FullName;
        const string rel = "intake/assets/vo/line.mp3";
        try
        {
            Directory.CreateDirectory(Path.Combine(content, "intake/assets/vo"));
            File.WriteAllText(Path.Combine(content, rel), "downloaded");
            File.WriteAllText(Path.Combine(content, "intake/assets/vo/private.json"), "{}");
            File.WriteAllText(Path.Combine(outside, "secret.mp3"), "secret");
            File.CreateSymbolicLink(Path.Combine(content, "intake/assets/vo/link.mp3"), Path.Combine(outside, "secret.mp3"));
            using var server = new WebAssetServer(web) { ContentRoot = () => content };
            using var http = new HttpClient(new HttpClientHandler { UseCookies = false });
            Assert.Equal(Path.GetFullPath(Path.Combine(content, rel)), server.ResolveFile("/" + rel));
            Assert.Equal(HttpStatusCode.Forbidden,
                (await http.GetAsync($"http://127.0.0.1:{server.Port}/{rel}")).StatusCode);
            var response = await http.GetAsync(server.Url(rel));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("audio/mpeg", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("downloaded", await response.Content.ReadAsStringAsync());
            Assert.Null(server.ResolveFile("/intake/assets/vo/private.json"));
            Assert.Null(server.ResolveFile("/intake/assets/vo/link.mp3"));
            Assert.Null(server.ResolveFile("/intake/assets/vo/../../../secret.mp3"));
            // Install-dir wins, including the Circe hotfix clips that deliberately override a stale pack.
            Directory.CreateDirectory(Path.Combine(web, "intake/assets/vo"));
            File.WriteAllText(Path.Combine(web, rel), "installed");
            Assert.Equal("installed", await (await http.GetAsync(server.Url(rel))).Content.ReadAsStringAsync());
            // A pack can arrive after the server was created; no stale readiness flag or restart.
            const string late = "intake/assets/vo/later.mp3";
            Assert.Null(server.ResolveFile("/" + late));
            File.WriteAllText(Path.Combine(content, late), "later");
            Assert.Equal("later", await (await http.GetAsync(server.Url(late))).Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(web, true);
            Directory.Delete(content, true);
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public Task InitIncludesOnlyTheEnabledSubliminalPool() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var settings = CoreSettings.Current;
        var previous = settings.SubliminalPool;
        var host = new IntakeHostWindow();
        try
        {
            settings.SubliminalPool = new() { [" present "] = true, ["disabled"] = false };
            var init = JObject.FromObject(host.InitMessage());
            var item = Assert.Single((JArray)init["config"]!["subliminals"]!);
            Assert.Equal("present", (string?)item["text"]);
            Assert.Equal(" present ", (string?)Assert.Single((JArray)init["config"]!["media"]!["subliminals"]!));
            var sprite = (string)init["config"]!["media"]!["bubbleSprite"]!;
            Assert.StartsWith("data:image/png;base64,", sprite);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, Convert.FromBase64String(sprite.Split(',')[1]).Take(4));
        }
        finally { settings.SubliminalPool = previous; host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReadyPostsInitThenTheActualFullscreenMode() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var host = new IntakeHostWindow { WindowState = WindowState.FullScreen };
        var sent = new System.Collections.Generic.List<JObject>();
        host.Posted += json => sent.Add(JObject.Parse(json));
        try
        {
            host.HandleMessage("{\"type\":\"ready\"}");
            Assert.Equal(new[] { "init", "fullscreen" }, sent.Select(m => (string?)m["type"]).ToArray());
            Assert.Equal(1, (int?)sent[0]["protocol"]);
            Assert.False((bool?)sent[0]["config"]!["endless"]);
            Assert.True((bool?)sent[1]["on"]);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task TheFallbackPanelNeverShowsTheToken()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var url = new Uri("http://127.0.0.1:5000/intake/index.html?ccp_t=ABC&x=1");
            const string expected = "http://127.0.0.1:5000/intake/index.html?x=1";
            Assert.Equal(expected, ConditioningControlPanel.Avalonia.Views.Controls.WebHost.WithoutToken(url));
            var host = new ConditioningControlPanel.Avalonia.Views.Controls.WebHost { Source = url };
            // The panel only draws without an engine (Linux CI); the Windows runner has WebView2.
            if (!ConditioningControlPanel.Avalonia.Views.Controls.WebHost.IsAvailable)
                Assert.Equal(expected, host.FindControl<global::Avalonia.Controls.TextBlock>("TxtSource")!.Text);
            // Either way, no text or tooltip anywhere in the host carries the token.
            foreach (var c in global::Avalonia.LogicalTree.LogicalExtensions.GetSelfAndLogicalDescendants(host).OfType<Control>())
            {
                Assert.DoesNotContain("ccp_t", (c as TextBlock)?.Text ?? "");
                Assert.DoesNotContain("ccp_t", ToolTip.GetTip(c)?.ToString() ?? "");
            }
            return Task.CompletedTask;
        });
    }
}
