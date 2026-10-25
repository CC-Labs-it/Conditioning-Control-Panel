using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralLoomTests
{
    private const string Gif = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    [Fact]
    public void Card_launches_singleton_loom_save_delete_refresh_library_and_panic_closes() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var root = Directory.CreateTempSubdirectory("ccp-loom-");
        using var server = new WebAssetServer(root.FullName);
        var previous = GameWindow.Server;
        GameWindow.Server = () => server;
        var card = new SpiralFeatureControl();
        var host = new Window { Content = card };
        host.Show();
        var slug = "test-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            var button = card.FindControl<Button>("BtnOpenLoom")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var loom = Assert.Single(GameWindow.OpenWindowsForTests().Where(w => w.Spec.Id == "loom"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(loom, Assert.Single(GameWindow.OpenWindowsForTests().Where(w => w.Spec.Id == "loom")));
            Assert.Equal("/dtrh/loom.html", loom.PageUrl!.AbsolutePath);
            var audio = CoreAudio.PlayOneShotProvider;
            var master = CoreSettings.Current.MasterVolume;
            try
            {
                var played = new List<(string Path, float Volume, string Tag)>();
                CoreAudio.PlayOneShotProvider = (path, volume, tag, _, _) => played.Add((path, volume, tag));
                CoreSettings.Current.MasterVolume = 50;
                loom.HandleMessage("{\"type\":\"sfx\",\"name\":\"time_slow_in\"}");
                var cue = Assert.Single(played);
                Assert.Equal("time_slow_in.mp3", Path.GetFileName(cue.Path));
                Assert.Equal(0.225f, cue.Volume, 5);
                Assert.Equal("chaos-sfx", cue.Tag);
                loom.HandleMessage("{\"type\":\"sfx\",\"name\":\"../outside\"}");
                Assert.Single(played);
            }
            finally { CoreAudio.PlayOneShotProvider = audio; CoreSettings.Current.MasterVolume = master; }
            var frames = new List<JObject>();
            loom.Posted += s => frames.Add(JObject.Parse(s));
            loom.Web.OnNavigationCompleted(loom.PageUrl);
            Assert.Equal("loom-list", (string?)Assert.Single(frames)["type"]);
            frames.Clear();
            loom.HandleMessage("{\"type\":\"ready\"}");
            Assert.Equal("loom-list", (string?)Assert.Single(frames)["type"]);
            frames.Clear();
            loom.HandleMessage(new JObject { ["type"] = "loom-save", ["name"] = slug,
                ["gifBase64"] = Gif, ["params"] = new JObject { ["speed"] = 3 } }.ToString());
            Assert.True((bool)frames[0]["ok"]!);
            Assert.Equal("loom-list", (string?)frames[1]["type"]);
            Dispatcher.UIThread.RunJobs();
            var library = card.FindControl<WrapPanel>("SpiralLibraryPanel")!;
            Assert.Contains(library.Children.OfType<Border>(), b => (string?)b.Tag == DtrhLoomStore.GifPathFor(slug));
            Assert.NotNull(server.ResolveFile("/ccp.spirals/loom_" + slug + ".gif"));
            frames.Clear();
            loom.HandleMessage(new JObject { ["type"] = "loom-save", ["name"] = slug, ["gifBase64"] = Gif }.ToString());
            Assert.Equal("exists", (string?)Assert.Single(frames)["error"]);
            frames.Clear();
            loom.HandleMessage(new JObject { ["type"] = "loom-delete", ["slug"] = slug }.ToString());
            Assert.True((bool)frames[0]["ok"]!);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(library.Children.OfType<Border>(), b => ((string?)b.Tag)?.Contains(slug) == true);
            // A mod switch rebuilds the default thumbnail even when this permanently mounted card stays open.
            var oldDefault = library.Children[0];
            var hero = card.FindControl<Border>("HeroArt")!;
            var side = card.FindControl<Border>("SideArt")!;
            var oldHero = Assert.IsType<ImageBrush>(hero.Background);
            var oldSide = Assert.IsType<ImageBrush>(side.Background);
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(oldDefault, library.Children[0]);
            Assert.NotSame(oldHero, Assert.IsType<ImageBrush>(hero.Background));
            Assert.NotSame(oldSide, Assert.IsType<ImageBrush>(side.Background));
            var games = Assert.Single(PanicSurfaces.All.Where(s => s.Id == "games"));
            games.Stop(null);
            Assert.DoesNotContain(loom, GameWindow.OpenWindowsForTests());
        }
        finally
        {
            DtrhLoomStore.Delete(slug);
            GameWindow.CloseAllForPanic();
            host.Close();
            GameWindow.Server = previous;
            root.Delete(true);
        }
    });

    [Fact]
    public void Spiral_route_only_serves_store_gifs_and_refuses_sidecars_traversal_and_links()
    {
        var root = Directory.CreateTempSubdirectory("ccp-loom-route-");
        using var server = new WebAssetServer(root.FullName);
        var slug = "test-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            Assert.True(DtrhLoomStore.Save(slug, Gif, new JObject(), false).Ok);
            Assert.Equal(DtrhLoomStore.GifPathFor(slug), server.ResolveFile("/ccp.spirals/loom_" + slug + ".gif"));
            foreach (var path in new[] { "loom_" + slug + ".json", "settings.json", "loom_../settings.gif", "loom_%2e%2e%2fsettings.gif" })
                Assert.Null(server.ResolveFile("/ccp.spirals/" + path));
            var gif = DtrhLoomStore.GifPathFor(slug)!;
            File.Delete(gif);
            var outside = Path.Combine(root.FullName, "outside.gif");
            File.WriteAllBytes(outside, Convert.FromBase64String(Gif));
            File.CreateSymbolicLink(gif, outside);
            Assert.Null(server.ResolveFile("/ccp.spirals/loom_" + slug + ".gif"));
        }
        finally { DtrhLoomStore.Delete(slug); root.Delete(true); }
    }
}
