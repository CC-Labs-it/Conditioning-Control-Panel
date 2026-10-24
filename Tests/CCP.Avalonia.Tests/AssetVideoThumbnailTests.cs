using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>AssetVideoThumbnail's off-Windows path decodes a real clip through the shared LibVLC
/// into a copied bitmap capped at 100 px (WPF ShellThumbnailHelper asks for 100x100), and a broken
/// file yields null so the tile keeps its video placeholder. Skips where libvlc is missing.</summary>
public sealed class AssetVideoThumbnailTests
{
    [Fact]
    public async Task DecodesACappedFrameAndReturnsNullForABrokenFile()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Windows uses the shell thumbnail provider");
        var dir = Directory.CreateTempSubdirectory("ccp-thumb-").FullName;
        // A 512x341 clip already in the repo: CI runners have libvlc but no ffmpeg to make one.
        var clip = Path.Combine(RepoRoot(), "Assets", "web", "dtrh", "assets", "bubbles", "spiral.webm");
        var broken = Path.Combine(dir, "broken.mp4");
        try
        {
            Assert.True(File.Exists(clip), clip);
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
            File.WriteAllBytes(broken, new byte[16]);

            PixelSize? size = null;
            var brokenIsNull = false;
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                using (var thumb = await AssetVideoThumbnail.LoadAsync(clip)) size = thumb?.PixelSize;
                var bad = await AssetVideoThumbnail.LoadAsync(broken);
                brokenIsNull = bad is null;
                bad?.Dispose();
            });
            Assert.Equal(new PixelSize(100, 67), size);
            Assert.True(brokenIsNull);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
