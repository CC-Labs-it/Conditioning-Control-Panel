using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBoonBarLiveTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    // WPF ChaosModeService.cs:422-437: the saved start boon is a live ribbon pick,
    // but a scripted first descent ignores even a stale equipped save.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Equipped_start_boon_lands_after_countdown_except_scripted_first_run(bool scripted)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var before = ChaosMeta.State.EquippedStartBoon;
            var resolver = ChaosRunState.IconResolver;
            try
            {
                CoreSettings.Current.ChaosAnnouncerEnabled = true;
                var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
                ChaosMeta.State.EquippedStartBoon = boon.Id;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, ScriptedFirstRun = scripted });
                Assert.Equal((Func<string, string, object?>)ChaosBoonArt.Resolve, ChaosRunState.IconResolver);
                Assert.Null(ChaosRunHost.BoonBar);
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                var bar = ChaosRunHost.BoonBar!;
                Assert.Equal(scripted ? 0 : 1, state.RunPickTiles.Count);
                Assert.Equal(scripted ? 0 : 1, bar.Row.Children.Count);
                Assert.Equal(!scripted, bar.Pill.IsVisible);
                if (!scripted)
                {
                    Assert.Equal(boon.Id, Assert.Single(state.RunPickTiles).Id);
                    var announcement = ChaosAnnouncerOverlay.Active!;
                    Assert.Equal(boon.Name, announcement.Current!.Text);
                    var mark = Assert.Single(announcement.GetLogicalDescendants().OfType<IconGlyph>());
                    Assert.Equal(FluentIcons.Common.Symbol.Diamond, mark.Kind);
                    Assert.Equal(FluentIcons.Common.IconVariant.Filled, mark.Variant);
                    Assert.False(mark.IsFallback);
                }
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosMeta.State.EquippedStartBoon = before;
                ChaosRunState.IconResolver = resolver;
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosArt.cs:28-68: precedence, shared decoded bitmap, corrupt/missing fallback.
    [Fact]
    public async Task Art_prefers_user_then_bundle_and_falls_back_for_unreadable_files()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var root = Path.Combine(Path.GetTempPath(), "ccp-boon-art-" + Guid.NewGuid());
            var user = Path.Combine(root, "user");
            var bundle = Path.Combine(root, "bundle");
            string Art(string dir, string id) => Path.Combine(dir, "assets", "Chaos", "boons", id + ".png");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Art(user, "pick"))!);
                Directory.CreateDirectory(Path.GetDirectoryName(Art(bundle, "pick"))!);
                using var small = new RenderTargetBitmap(new PixelSize(2, 2));
                using var large = new RenderTargetBitmap(new PixelSize(3, 3));
                small.Save(Art(user, "pick"));
                large.Save(Art(bundle, "pick"));
                large.Save(Art(bundle, "bad"));
                File.WriteAllText(Art(user, "bad"), "invalid image");
                var image = ChaosBoonArt.ResolveFrom("boons", "pick", user, bundle);
                Assert.Equal(new PixelSize(2, 2), image!.PixelSize);
                Assert.Same(image, ChaosBoonArt.ResolveFrom("boons", "pick", user, bundle));
                Assert.Equal(new PixelSize(3, 3), ChaosBoonArt.ResolveFrom("boons", "bad", user, bundle)!.PixelSize);
                Assert.Null(ChaosBoonArt.ResolveFrom("boons", "absent", user, bundle));
            }
            finally { Directory.Delete(root, true); }
            return Task.CompletedTask;
        });
    }
    // WPF ConditioningControlPanel.csproj:206-214 ships promoted art, not staging candidates.
    [Fact]
    public async Task Bundled_boon_art_is_copied_to_real_output_and_resolves()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var source = Path.Combine(RepoRoot(), "ConditioningControlPanel", "assets", "Chaos", "boons");
            var output = Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", "boons");
            var expected = Directory.GetFiles(source, "*.png", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(source, p))
                .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith("_staging", StringComparison.Ordinal)))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(expected);
            Assert.True(Directory.Exists(output), "Bundled boon art was not copied to output");
            var copied = Directory.GetFiles(output, "*.png", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(output, p)).OrderBy(p => p, StringComparer.Ordinal);
            Assert.Equal(expected, copied);
            var image = Assert.IsType<Bitmap>(ChaosBoonArt.ResolveFrom("boons", "welcome_shower", AppContext.BaseDirectory));
            Assert.Equal(new PixelSize(512, 512), image.PixelSize);
            return Task.CompletedTask;
        });
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

}
