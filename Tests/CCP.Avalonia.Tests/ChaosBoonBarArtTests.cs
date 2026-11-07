using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBoonBarArtTests
{
    [Fact]
    public async Task Drafted_boon_and_sin_show_bundled_art_and_missing_art_shows_glyph()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var previous = ChaosRunState.IconResolver;
            try
            {
                ChaosRunState.IconResolver = ChaosBoonBarArt.Resolve;
                var state = new ChaosRunState(new ChaosRunConfig());
                state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "defuse_chain"));
                state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "hair_trigger"));
                var bar = ChaosBoonBarOverlay.ForRun();
                bar.SetPicks(state.RunPickTiles);
                Assert.All(bar.Row.Children, tile => Assert.IsType<Image>(((Panel)((Border)tile).Child!).Children[0]));
                bar.SetPicks(new[] { new ChaosSidebarBoon { Glyph = "?" } });
                Assert.IsType<IconGlyph>(((Panel)((Border)bar.Row.Children[0]).Child!).Children[0]);
                bar.Close();
            }
            finally { ChaosRunState.IconResolver = previous; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task User_art_wins_and_unreadable_or_missing_art_falls_back()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var user = Path.Combine(root, "user");
            var app = Path.Combine(root, "app");
            var userDir = Path.Combine(user, "assets", "Chaos", "boons");
            var appDir = Path.Combine(app, "assets", "Chaos", "boons");
            Directory.CreateDirectory(userDir);
            Directory.CreateDirectory(appDir);
            try
            {
                var shipped = Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", "boons", "defuse_chain.png");
                File.Copy(shipped, Path.Combine(appDir, "pick.png"));
                File.WriteAllBytes(Path.Combine(userDir, "pick.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="));
                using var art = ChaosBoonBarArt.Resolve("boons", "pick", user, app);
                Assert.NotNull(art);
                Assert.Equal(1, art.PixelSize.Width);
                File.WriteAllText(Path.Combine(userDir, "pick.png"), "bad image");
                using var fallback = ChaosBoonBarArt.Resolve("boons", "pick", user, app);
                Assert.NotNull(fallback);
                Assert.True(fallback.PixelSize.Width > 1);
                File.WriteAllText(Path.Combine(appDir, "pick.png"), "bad image");
                Assert.Null(ChaosBoonBarArt.Resolve("boons", "pick", user, app));
                Assert.Null(ChaosBoonBarArt.Resolve("boons", "missing", user, app));
            }
            finally { Directory.Delete(root, true); }
            return Task.CompletedTask;
        });
    }
}
