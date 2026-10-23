using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Showcase;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 079d665ab/08e0c1e8c: the premium showcase rides the Tonight Board - its provider is
/// in the deck after the house cards, its card wears the clip art over its poster, Motion Off
/// starts no player, and a closed shell frees the art.</summary>
public sealed class BillboardShowcaseTests
{
    [Fact]
    public Task ShowcaseCardWearsItsPosterOnHomeAndLetsGoOnClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var old = CoreSettings.ServiceProvider;
        var oldMake = MainShellWindow.MakeShowcase;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Off;   // no ease, and no clip may move
        s.PerformanceMode = false;
        s.BillboardSnoozedUntil.Clear();

        var dir = Directory.CreateTempSubdirectory("ccp-showcase-").FullName;
        var poster = Path.Combine(dir, "dtrh.png");
        using (var bmp = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul)) bmp.Save(poster);
        var video = Path.Combine(dir, "dtrh.mp4");
        File.WriteAllBytes(video, new byte[] { 0 });
        var clip = new ShowcaseClip("dtrh", ShowcaseTier.Prime, "showcase-dtrh.mp4", 1, "", "showcase-dtrh.jpg", 1, "", 6);
        string? Copy(string key) { var v = Loc.Get(key); return v == key ? null : v; }
        var card = ShowcaseRules.BuildCard(clip, new ShowcaseClipArt("dtrh", video, poster, null), Copy);
        Assert.NotNull(card);
        var fake = new FakeShowcase(card);
        MainShellWindow.MakeShowcase = () => fake;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.BillboardHost!;
            Assert.Equal(new[] { "house.discord", "house.webapp", "showcase:dtrh" }, host.Deck.Cards.Select(c => c.Spec.Id).Order());
            while (host.CurrentCard?.Spec.Id != "showcase:dtrh") host.Advance();
            Dispatcher.UIThread.RunJobs();

            var art = Assert.Single(host.GetLogicalDescendants().OfType<ClipArtView>());
            Assert.NotNull(art.Poster.Source);
            Assert.False(art.HasPlayer);   // Motion Off: the poster stands, no decoder made

            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(art.Poster.Source);   // the art let go with the shell (P68)
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            MainShellWindow.MakeShowcase = oldMake;
            s.BillboardSnoozedUntil.Clear();
            s.MotionLevel = MotionLevel.Full;
            CoreSettings.ServiceProvider = old;
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF ClipArtView with no clip: the card still wears the clip art, on its plain dark
    /// ground, and makes no player even where motion is allowed. A regression guard: it already passes
    /// on the base; fail-proven by changing the ground colour in ClipArtView.</summary>
    [Fact]
    public Task ShowcaseCardWithoutAClipStandsOnItsPlainGround() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ClipArtView.MotionAllowed = () => true;
        string? Copy(string key) { var v = Loc.Get(key); return v == key ? null : v; }
        var clip = new ShowcaseClip("dtrh", ShowcaseTier.Prime, "showcase-dtrh.mp4", 1, "", "showcase-dtrh.jpg", 1, "", 6);
        EnsureApp();
        var card = ShowcaseRules.BuildCard(clip, null, Copy);
        Assert.NotNull(card);
        var shell = ShowHome(card, out var undo);
        try
        {
            var art = Assert.Single(shell.BillboardHost!.GetLogicalDescendants().OfType<ClipArtView>());
            Assert.Null(art.Poster.Source);
            Assert.False(art.HasPlayer);
            Assert.Equal(Color.FromRgb(0x0c, 0x0d, 0x1a), Assert.IsType<SolidColorBrush>(art.Background).Color);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); undo(); }
        return Task.CompletedTask;
    });

    /// <summary>A real clip decodes on Home, and the view holds its decoder the moment it leaves the
    /// visual tree, whoever takes it off; closing the shell then frees it. Skips where ffmpeg (to
    /// make the clip) or libvlc is missing.</summary>
    [Fact]
    public async Task ShowcaseClipDecodesOnHomeAndHoldsWhenDetached()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-showcase-").FullName;
        var video = Path.Combine(dir, "dtrh.mp4");
        try
        {
            try
            {
                using var ff = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-v error -f lavfi -i testsrc=d=10:s=320x240:r=25 -pix_fmt yuv420p \"{video}\"") { UseShellExecute = false })!;
                ff.WaitForExit();
            }
            catch (Exception) { }
            if (!File.Exists(video)) Assert.Skip("ffmpeg not available to generate a test clip");
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }

            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                ClipArtView.MotionAllowed = () => true;
                string? Copy(string key) { var v = Loc.Get(key); return v == key ? null : v; }
                var clip = new ShowcaseClip("dtrh", ShowcaseTier.Prime, "showcase-dtrh.mp4", 1, "", "showcase-dtrh.jpg", 1, "", 6);
                var played = new List<string>();
                EnsureApp();
                var card = ShowcaseRules.BuildCard(clip, new ShowcaseClipArt("dtrh", video, null, played.Add), Copy);
                Assert.NotNull(card);
                var shell = ShowHome(card, out var undo);
                try
                {
                    var art = Assert.Single(shell.BillboardHost!.GetLogicalDescendants().OfType<ClipArtView>());
                    Assert.True(await Until(() => art.Decoding && played.Count == 1), "the clip never decoded on Home");

                    ((Panel)art.Parent!).Children.Remove(art);
                    Assert.True(await Until(() => !art.Decoding), "the clip kept decoding off the visual tree");
                    Assert.True(art.HasPlayer);   // held, not freed: a Run resumes it

                    shell.Close();
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(art.HasPlayer);   // the slide still carries its art: the shell frees it (P68)
                }
                finally { shell.Close(); Dispatcher.UIThread.RunJobs(); undo(); }
            });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static async Task<bool> Until(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++) await Task.Delay(50);
        return done();
    }

    /// <summary>The test app (whose Initialize loads the strings) exists before a card is built.</summary>
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>A shown shell whose deck is the house cards plus this showcase card, turned to it.
    /// Motion Off keeps the deck still; <see cref="ClipArtView.MotionAllowed"/> decides the clip.</summary>
    private static MainShellWindow ShowHome(BillboardCardSpec card, out Action undo)
    {
        EnsureApp();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Off;
        s.PerformanceMode = false;
        s.BillboardSnoozedUntil.Clear();
        MainShellWindow.MakeShowcase = () => new FakeShowcase(card);
        undo = () =>
        {
            s.BillboardSnoozedUntil.Clear();
            s.MotionLevel = MotionLevel.Full;
            CoreSettings.ServiceProvider = old;
        };

        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        var host = shell.BillboardHost!;
        while (host.CurrentCard?.Spec.Id != card.Id) host.Advance();
        Dispatcher.UIThread.RunJobs();
        return shell;
    }

    /// <summary>The board stays silent: the clip player is muted and opens no audio track.</summary>
    [Fact]
    public void ShowcaseClipIsMutedWithNoAudioTrack()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ConditioningControlPanel.sln"))) d = d.Parent;
        var code = File.ReadAllText(Path.Combine(d!.FullName, "CCP.Avalonia/Controls/Billboard/ClipArtView.cs"));
        code = Regex.Replace(code, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        Assert.Matches(@"new MediaPlayer\(vlc\)\s*\{[^}]*Mute = true", code);
        Assert.Contains("AddOption(ConditioningControlPanel.Services.LibVlcSilence.NoAudioOption)", code, StringComparison.Ordinal);
    }

    private sealed class FakeShowcase(BillboardCardSpec card) : IBillboardProvider
    {
        public string Id => "showcase";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) =>
            context.Tier == BillboardTier.Prime ? Array.Empty<BillboardCardSpec>() : new[] { card };
        public void Invoke(string actionTarget) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
