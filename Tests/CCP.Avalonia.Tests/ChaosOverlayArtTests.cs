using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ConditioningControlPanel.Services.Chaos;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosOverlayArtTests
{
    [Fact]
    public async Task User_art_wins_and_missing_or_invalid_art_falls_back()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var user = Path.Combine(dir, "user");
            var bundled = Path.Combine(dir, "bundle");
            var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
            string Put(string root, string name, byte[] data)
            {
                var path = Path.Combine(root, "assets", "Chaos", name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
                return path;
            }
            try
            {
                Put(bundled, "recap.png", bytes);
                var bundledImage = ChaosOverlayWindow.ChaosArt.ResolvePath("recap.png", bundled);
                Assert.NotNull(bundledImage);
                Assert.Same(bundledImage, ChaosOverlayWindow.ChaosArt.ResolvePath("recap.png", user, bundled));
                Put(user, "recap.png", bytes);
                var userImage = ChaosOverlayWindow.ChaosArt.ResolvePath("recap.png", user, bundled);
                Assert.NotNull(userImage);
                Assert.NotSame(bundledImage, userImage);
                Put(user, "boons/bad.png", new byte[] { 1, 2, 3 });
                Put(bundled, "boons/bad.png", bytes);
                Assert.NotNull(ChaosOverlayWindow.ChaosArt.ResolvePath("boons/bad.png", user, bundled));
                Assert.Null(ChaosOverlayWindow.ChaosArt.ResolvePath("portraits/missing.png", user, bundled));
            }
            finally { Directory.Delete(dir, true); }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Recap_boon_and_portrait_use_present_art()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Path.Combine(Path.GetTempPath(), "ccp-chaos-art-" + Guid.NewGuid().ToString("N"));
            var root = Path.Combine(dir, "assets", "Chaos");
            var id = "test_" + Guid.NewGuid().ToString("N");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
            var paths = new[] { Path.Combine(root, "recap.png"), Path.Combine(root, "boons", id + ".png"), Path.Combine(root, "portraits", id + ".png") };
            ChaosOverlayWindow? window = null;
            try
            {
                foreach (var path in paths) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, png); }
                window = ChaosOverlayWindow.ForRun(new[] { dir });
                var state = new ChaosRunState(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                window.ShowResults(state, 0, 1, 0, 0, 0);
                var recap = ChaosOverlayWindow.ChaosArt.ResolvePath("recap.png", dir);
                Assert.NotNull(recap);
                Assert.Same(recap, window.FindControl<Image>("ResultsHero")!.Source);
                var boon = new ChaosBoon(id, "test", "test", ChaosRarity.Common, false, 0, _ => { });
                window.ShowBoonDraft(1, new List<ChaosBoon> { boon }, _ => { });
                Assert.Contains(window.FindControl<StackPanel>("BoonCardHost")!.GetLogicalDescendants().OfType<Border>(), b => b.Background is ImageBrush);
                window.ShowConversation(new ChaosConversation { PortraitId = id, Lines = new() { new ChaosConversationLine() } }, null, null);
                Assert.NotNull(window.FindControl<Image>("StoryPortrait")!.Source);
                Assert.True(window.FindControl<Image>("StoryPortrait")!.IsVisible);
            }
            finally
            {
                try { window?.Close(); }
                finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Shipped_art_resolves_from_build_output()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            foreach (var relative in new[] { "recap.png", "boons/aftermath.png", "portraits/madam.png" })
            {
                Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", relative)), relative);
                Assert.NotNull(ChaosOverlayWindow.ChaosArt.ResolvePath(relative, AppContext.BaseDirectory));
            }
            return Task.CompletedTask;
        });
    }

}
