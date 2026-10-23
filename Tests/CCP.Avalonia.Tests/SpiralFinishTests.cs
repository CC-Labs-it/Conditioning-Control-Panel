using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralFinishTests
{
    private const string Gif = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    [Fact]
    public void Loom_thumbnails_serve_only_store_gifs_and_refuse_traversal_and_links()
    {
        var root = Directory.CreateTempSubdirectory("loom-web-");
        var slug = "test-" + Guid.NewGuid().ToString("N")[..12];
        Assert.True(DtrhLoomStore.Save(slug, Gif, null, false).Ok);
        using var server = new WebAssetServer(root.FullName);
        var gif = DtrhLoomStore.GifPathFor(slug)!;
        var link = Path.Combine(DtrhLoomStore.SpiralsFolder, "loom_link.gif");
        try
        {
            Assert.Equal(gif, server.ResolveFile("/__spirals__/loom_" + slug + ".gif"));
            Assert.Null(server.ResolveFile("/__spirals__/loom_" + slug + ".json"));
            Assert.Null(server.ResolveFile("/__spirals__/../settings.json"));
            Assert.Null(server.ResolveFile("/__spirals__/sub/loom_" + slug + ".gif"));
            Assert.Null(server.ResolveFile("/__spirals__/not-a-loom.gif"));
            var outside = Path.Combine(root.FullName, "outside.gif");
            File.WriteAllBytes(outside, Convert.FromBase64String(Gif));
            File.CreateSymbolicLink(link, outside);
            Assert.Null(server.ResolveFile("/__spirals__/loom_link.gif"));
        }
        finally { File.Delete(link); DtrhLoomStore.Delete(slug); root.Delete(true); }
    }

    [Fact]
    public Task Loom_button_opens_one_window_ready_save_overwrite_delete_reveal_and_panic()
        => Run(async () =>
        {
            var root = Directory.CreateTempSubdirectory("loom-host-");
            using var server = new WebAssetServer(root.FullName);
            var oldServer = GameWindow.Server;
            var oldShell = ExternalOpener.Shell;
            var oldAudio = CoreAudio.PlayOneShotProvider;
            var oldAudioPath = CoreModArt.AudioOverridePathProvider;
            var oldVolume = CoreSettings.Current.MasterVolume;
            var savedPath = CoreSettings.Current.SpiralPath;
            GameWindow.Server = () => server;
            var card = new SpiralFeatureControl();
            var host = new Window { Content = card };
            var slug = "test-" + Guid.NewGuid().ToString("N")[..12];
            string? revealed = null;
            ExternalOpener.Shell = path => { revealed = path; return true; };
            host.Show();
            try
            {
                var button = card.FindControl<Button>("BtnOpenLoom")!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var loom = Assert.IsType<GameWindow>(GameWindow.OpenForTests("loom"));
                Assert.Equal("/dtrh/loom.html", loom.PageUrl!.AbsolutePath);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(GameWindow.OpenWindowsForTests(), w => w.Spec.Id == "loom");
                var messages = new System.Collections.Generic.List<JObject>();
                loom.Posted += json => messages.Add(JObject.Parse(json));
                loom.Web.OnNavigationCompleted(loom.PageUrl);
                void Send(object frame) => loom.Web.OnWebMessage(Newtonsoft.Json.JsonConvert.SerializeObject(frame));
                Send(new { type = "ready" });
                Assert.Equal("loom-list", (string?)Assert.Single(messages)["type"]);
                Send(new { type = "loom-save", name = slug, gifBase64 = Gif, @params = new { arms = 3 } });
                Assert.True((bool)messages.Last(m => (string?)m["type"] == "loom-result")["ok"]!);
                await Task.Yield();
                var path = DtrhLoomStore.GifPathFor(slug)!;
                var panel = card.FindControl<WrapPanel>("SpiralLibraryPanel")!;
                Assert.Contains(panel.Children.OfType<Border>(), c => (string?)c.Tag == path);
                var list = messages.Last(m => (string?)m["type"] == "loom-list");
                var entry = list["spirals"]!.Single(s => (string?)s["slug"] == slug);
                Assert.Equal(3, (int)entry["params"]!["arms"]!);
                Assert.Equal(path, server.ResolveFile(new Uri((string)entry["url"]!).AbsolutePath));
                Send(new { type = "loom-save", name = slug, gifBase64 = Gif });
                Assert.False((bool)messages.Last(m => (string?)m["type"] == "loom-result")["ok"]!);
                Send(new { type = "loom-save", name = slug, gifBase64 = Gif, overwrite = true });
                Assert.True((bool)messages.Last(m => (string?)m["type"] == "loom-result")["ok"]!);
                Send(new { type = "loom-reveal", slug = "../outside" });
                Assert.Null(revealed);
                Send(new { type = "loom-reveal", slug });
                Assert.Equal(Path.GetDirectoryName(path), revealed); // portable file manager opens the containing folder
                float? cueVolume = null;
                CoreSettings.Current.MasterVolume = 50;
                CoreModArt.AudioOverridePathProvider = p => p == "chaos/test.mp3" ? path : null;
                CoreAudio.PlayOneShotProvider = (_, volume, _, _, _) => cueVolume = volume;
                Send(new { type = "sfx", name = "../outside" });
                Assert.Null(cueVolume);
                Send(new { type = "sfx", name = "test" });
                Assert.Equal(0.225f, cueVolume!.Value, 4);
                CoreSettings.Current.SpiralPath = path;
                Send(new { type = "loom-delete", slug });
                await Task.Yield();
                Assert.Equal("", CoreSettings.Current.SpiralPath);
                Assert.DoesNotContain(panel.Children.OfType<Border>(), c => (string?)c.Tag == path);
                var panic = ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(p => p.Id == "games");
                panic.Stop(null);
                Assert.Null(GameWindow.OpenForTests("loom"));
            }
            finally
            {
                GameWindow.CloseAllForPanic(); host.Close();
                GameWindow.Server = oldServer; ExternalOpener.Shell = oldShell;
                CoreAudio.PlayOneShotProvider = oldAudio; CoreModArt.AudioOverridePathProvider = oldAudioPath;
                CoreSettings.Current.MasterVolume = oldVolume;
                DtrhLoomStore.Delete(slug); CoreSettings.Current.SpiralPath = savedPath;
                root.Delete(true);
            }
        });

    [Fact]
    public Task Mod_switch_repaints_spiral_plates_and_default_thumbnail_while_attached()
        => Run(() =>
        {
            var root = Directory.CreateTempSubdirectory("spiral-art-");
            var png = Path.Combine(root.FullName, "art.png");
            using (var bmp = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96))) bmp.Save(png);
            var previous = CoreModArt.OverridePathProvider;
            var card = new SpiralFeatureControl();
            var window = new Window { Content = card };
            try
            {
                window.Show();
                Assert.IsType<ImageBrush>(card.FindControl<Border>("HeroArt")!.Background);
                CoreModArt.OverridePathProvider = p => p is "features/spiral_overlay.png" or "spirals/spiral.gif" ? png : null;
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                var hero = (ImageBrush)card.FindControl<Border>("HeroArt")!.Background!;
                var side = (ImageBrush)card.FindControl<Border>("SideArt")!.Background!;
                Assert.Equal(400, ((Bitmap)hero.Source!).PixelSize.Height);
                Assert.Same(hero.Source, side.Source);
                var gallery = card.FindControl<WrapPanel>("SpiralLibraryPanel")!;
                var defaultCard = (Border)gallery.Children[0];
                var image = (Image)((Border)((StackPanel)defaultCard.Child!).Children[0]).Child!;
                Assert.Equal(new PixelSize(120, 60), ((Bitmap)image.Source!).PixelSize);
                window.Content = null;
                var brush = hero;
                CoreModArt.OverridePathProvider = null;
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                Assert.Same(brush, card.FindControl<Border>("HeroArt")!.Background);
            }
            finally { window.Close(); CoreModArt.OverridePathProvider = previous; root.Delete(true); }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Narrow_card_hides_side_art_with_hysteresis_and_picker_confirms_the_filename()
        => Run(async () =>
        {
            var root = Directory.CreateTempSubdirectory("spiral-pick-");
            var gif = Path.Combine(root.FullName, "picked.gif");
            File.WriteAllBytes(gif, Convert.FromBase64String(Gif));
            var card = new SpiralFeatureControl();
            var window = new Window { Content = card, Width = 650, Height = 700 };
            var previous = CoreSettings.Current.SpiralPath;
            var enabled = CoreSettings.Current.SpiralEnabled;
            CoreSettings.Current.SpiralEnabled = false;
            try
            {
                window.Show();
                var grid = card.FindControl<Grid>("SettingsAndArt")!;
                foreach (var (width, visible) in new[] { (699, false), (720, false), (748, true), (700, true), (699, false) })
                {
                    grid.Arrange(new Rect(0, 0, width, 700));
                    Assert.Equal(visible, card.FindControl<Border>("SideArt")!.IsVisible);
                    Assert.Equal(visible ? 780 : double.PositiveInfinity, grid.ColumnDefinitions[0].MaxWidth);
                }
                var picked = card.SelectPickedSpiral(gif);
                await Task.Yield();
                var confirmation = Assert.Single(window.OwnedWindows);
                Assert.Equal("Spiral Selected", confirmation.Title);
                Assert.Equal("Selected: picked.gif", confirmation.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.Equal(gif, CoreSettings.Current.SpiralPath);
                confirmation.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await picked;
            }
            finally
            {
                foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
                window.Close(); CoreSettings.Current.SpiralPath = previous;
                CoreSettings.Current.SpiralEnabled = enabled; root.Delete(true);
            }
        });

    [Fact]
    public Task Video_selection_repaints_without_restart_switches_to_gif_and_stops_on_panic()
        => Run(async () =>
        {
            var root = Directory.CreateTempSubdirectory("spiral-video-");
            var video = Path.Combine(root.FullName, "loop.mp4");
            File.WriteAllBytes(video, new byte[] { 1 }); // native decoder is replaced, window lifecycle is real
            var gif = Path.Combine(root.FullName, "still.gif");
            File.WriteAllBytes(gif, Convert.FromBase64String(Gif));
            var s = CoreSettings.Current;
            var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralRandomize);
            var oldEngine = CoreSession.IsEngineRunningProvider;
            var oldStart = SpiralOverlay.VideoStart;
            var oldChecks = SpiralOverlay.SkipPlatformChecksForTests;
            var host = new Window();
            using var bitmap = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96));
            int starts = 0, stops = 0;
            Action<Bitmap>? frame = null;
            SpiralOverlay.VideoStart = (path, show) =>
            {
                Assert.Equal(video, path);
                starts++; frame = show;
                return new OnDispose(() => stops++);
            };
            try
            {
                host.Show();
                CoreSession.IsEngineRunningProvider = null;
                SpiralOverlay.SkipPlatformChecksForTests = true;
                if (SpiralOverlay.Decoding is { } pending) await pending;
                SpiralOverlay.CloseAll();
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralRandomize) = (true, video, 40, false);
                SpiralOverlay.Refresh(host);
                Assert.True(SpiralOverlay.IsVideoPlaying);
                Assert.False(SpiralOverlay.IsAnimating); // LibVLC drives frames; no GIF timer
                frame!(bitmap);
                Assert.NotEmpty(SpiralOverlay.Shown);
                Assert.All(SpiralOverlay.Shown, w => Assert.Same(bitmap, w.Spiral.Source));
                s.SpiralOpacity = 90;
                SpiralOverlay.Refresh(host);
                Assert.Equal(1, starts);
                Assert.All(SpiralOverlay.Shown, w => Assert.Equal(ConditioningControlPanel.Services.SpiralFrames.Paint(0.9), w.Spiral.Opacity, 10));
                s.SpiralPath = gif;
                SpiralOverlay.Refresh(host);
                if (SpiralOverlay.Decoding is { } decode) await decode;
                Assert.Equal(1, stops);
                Assert.False(SpiralOverlay.IsVideoPlaying);
                Assert.NotEmpty(SpiralOverlay.Shown);
                Assert.NotSame(bitmap, SpiralOverlay.Shown[0].Spiral.Source);
                s.SpiralPath = video;
                SpiralOverlay.Refresh(host);
                Assert.Equal(2, starts);
                // Same teardown used by engine stop, session pause, panic and exit.
                global::ConditioningControlPanel.Avalonia.App.StopDesktopOverlays();
                Assert.Equal(2, stops);
                Assert.False(SpiralOverlay.IsVideoPlaying || SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
                SpiralOverlay.Refresh(host);
                s.SpiralEnabled = false;
                SpiralOverlay.Refresh(host);
                Assert.Equal(3, stops);
            }
            finally
            {
                SpiralOverlay.CloseAll(); host.Close();
                SpiralOverlay.VideoStart = oldStart;
                SpiralOverlay.SkipPlatformChecksForTests = oldChecks;
                CoreSession.IsEngineRunningProvider = oldEngine;
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralRandomize) = saved;
                root.Delete(true);
            }
        });

    [Fact]
    public Task Native_video_keeps_delivering_frames_after_the_clip_duration()
        => Run(async () =>
        {
            // CI has LibVLC; the cloud font/native limitation is recorded in the unit's verification draft.
            if (LibVlcAudio.Shared == null) _ = new LibVlcAudio("--aout=dummy");
            var s = CoreSettings.Current;
            var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize);
            var engine = CoreSession.IsEngineRunningProvider;
            var checks = SpiralOverlay.SkipPlatformChecksForTests;
            var host = new Window();
            try
            {
                host.Show();
                CoreSession.IsEngineRunningProvider = null;
                SpiralOverlay.SkipPlatformChecksForTests = true;
                if (SpiralOverlay.Decoding is { } pending) await pending;
                SpiralOverlay.CloseAll();
                (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize) = (true,
                    Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), false);
                SpiralOverlay.Refresh(host);
                await Task.Delay(3000, TestContext.Current.CancellationToken);
                var frames = SpiralOverlay.VideoFrames;
                Assert.True(frames > 0);
                await Task.Delay(3000, TestContext.Current.CancellationToken);
                Assert.True(SpiralOverlay.VideoFrames > frames); // a non-looped 2s clip has already stopped
                Assert.IsType<WriteableBitmap>(SpiralOverlay.Shown[0].Spiral.Source);
            }
            finally
            {
                SpiralOverlay.CloseAll(); host.Close();
                (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize) = saved;
                CoreSession.IsEngineRunningProvider = engine;
                SpiralOverlay.SkipPlatformChecksForTests = checks;
            }
        });

    private sealed class OnDispose(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        await body();
    });
}
