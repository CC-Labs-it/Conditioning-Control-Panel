using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LoomFeatureTests
{
    [Fact]
    public async System.Threading.Tasks.Task Saved_gif_route_requires_auth_and_never_serves_sidecars_or_escaping_links()
    {
        var root = Directory.CreateTempSubdirectory("loom-server-").FullName;
        const string slug = "fin5-route";
        using var server = new WebAssetServer(root);
        var bytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        try
        {
            Assert.True(DtrhLoomStore.Save(slug, Convert.ToBase64String(bytes), new JObject(), true).Ok);
            var url = server.Url($"ccp.spirals/loom_{slug}.gif");
            using var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { UseCookies = false, UseProxy = false });
            Assert.Equal(bytes, await client.GetByteArrayAsync(url));
            using var denied = await client.GetAsync(new Uri(url).GetLeftPart(UriPartial.Path));
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Null(server.ResolveFile($"/ccp.spirals/loom_{slug}.json"));
            Assert.Null(server.ResolveFile("/ccp.spirals/../settings.json"));
            var gif = DtrhLoomStore.GifPathFor(slug)!;
            File.Delete(gif);
            var outside = Path.Combine(root, "outside.gif");
            File.WriteAllBytes(outside, bytes);
            File.CreateSymbolicLink(gif, outside);
            Assert.Null(server.ResolveFile($"/ccp.spirals/loom_{slug}.gif"));
        }
        finally
        {
            DtrhLoomStore.Delete(slug);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Card_opens_singleton_loom_save_delete_refresh_gallery_and_panic_closes() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldServer = GameWindow.Server;
        var oldOpen = ExternalOpener.Shell;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldCue = CoreModArt.AudioOverridePathProvider;
        CoreSettings.ServiceProvider = () => service;
        var root = Directory.CreateTempSubdirectory("loom-web-").FullName;
        using var server = new WebAssetServer(root);
        GameWindow.Server = () => server;
        var card = new SpiralFeatureControl();
        var owner = new Window { Content = card };
        const string slug = "fin5-test";
        DtrhLoomStore.Delete(slug);
        try
        {
            owner.Show();
            var button = card.FindControl<Button>("BtnOpenLoom")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var loom = Assert.Single(GameWindow.OpenWindowsForTests());
            Assert.Equal("loom", loom.Spec.Id);
            Assert.Equal("The Loom", loom.Title);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(loom, Assert.Single(GameWindow.OpenWindowsForTests()));
            var frames = new List<JObject>();
            loom.Posted += json => frames.Add(JObject.Parse(json));
            loom.Web.OnNavigationCompleted(loom.PageUrl);
            void Send(object message) => loom.Web.OnWebMessage(JsonConvert.SerializeObject(message));
            loom.Web.OnNavigationCompleted(new Uri("https://example.invalid/"));
            Send(new { type = "loom-delete", slug });
            Assert.Empty(frames);
            loom.Web.OnNavigationCompleted(loom.PageUrl);
            Send(new { type = "ready" });
            Assert.Equal("loom-list", (string?)Assert.Single(frames)["type"]);
            frames.Clear();
            Send(new { type = "loom-save", name = slug, gifBase64 = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", @params = new { speed = 2 } });
            Assert.True((bool)frames[0]["ok"]!);
            Assert.Equal("save", (string?)frames[0]["op"]);
            var item = frames[1]["spirals"]!.Single(x => (string?)x["slug"] == slug);
            Assert.Equal(2, (int)item["params"]!["speed"]!);
            var url = new Uri((string)item["url"]!);
            Assert.Equal(DtrhLoomStore.GifPathFor(slug), server.ResolveFile(url.AbsolutePath));
            string? revealed = null;
            ExternalOpener.Shell = path => { revealed = path; return true; };
            Send(new { type = "loom-reveal", slug });
            Assert.Equal(DtrhLoomStore.GifPathFor(slug), revealed);
            revealed = null;
            Send(new { type = "loom-reveal", slug = "../settings" });
            Assert.Null(revealed);
            var cue = Path.Combine(root, "cue.mp3");
            File.WriteAllBytes(cue, new byte[] { 0 });
            CoreModArt.AudioOverridePathProvider = rel => rel == "chaos/test.mp3" ? cue : null;
            float? volume = null;
            CoreAudio.PlayOneShotProvider = (_, v, _, _, _) => volume = v;
            CoreSettings.Current.MasterVolume = 50;
            Send(new { type = "sfx", name = "test", scale = .4 });
            Assert.Equal(.2f, volume);
            volume = null;
            Send(new { type = "sfx", name = "../test" });
            Assert.Null(volume);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Contains(card.FindControl<WrapPanel>("SpiralLibraryPanel")!.Children.OfType<Border>(),
                x => (string?)x.Tag == DtrhLoomStore.GifPathFor(slug));
            frames.Clear();
            Send(new { type = "loom-save", name = slug, gifBase64 = "bad" });
            Assert.False((bool)frames[0]["ok"]!);
            Assert.Equal("exists", (string?)frames[0]["error"]);
            Assert.Single(frames); // failed saves never send a replacement list
            frames.Clear();
            Send(new { type = "loom-delete", slug });
            Assert.True((bool)frames[0]["ok"]!);
            Assert.Empty(frames[1]["spirals"]!.Where(x => (string?)x["slug"] == slug));
            Assert.Null(server.ResolveFile("/ccp.spirals/../settings.json"));
            Assert.Null(server.ResolveFile("/ccp.spirals/loom_../settings.gif"));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(card.FindControl<WrapPanel>("SpiralLibraryPanel")!.Children.OfType<Border>(),
                x => (string?)x.Tag == DtrhLoomStore.GifPathFor(slug));
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(null!);
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            owner.Close();
            DtrhLoomStore.Delete(slug);
            CoreSettings.ServiceProvider = oldSettings;
            GameWindow.Server = oldServer;
            ExternalOpener.Shell = oldOpen;
            CoreAudio.PlayOneShotProvider = oldAudio;
            CoreModArt.AudioOverridePathProvider = oldCue;
            Directory.Delete(root, true);
        }
    });
}
