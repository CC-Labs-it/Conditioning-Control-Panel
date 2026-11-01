using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class VideoThumbnailVlcTests
{
    [Fact]
    public async Task DecodesARealVideoWithoutFfmpegOnPath()
    {
        var previousVlc = LibVlcAudio.Shared;
        LibVlcAudio? created = null;
        try
        {
            if (previousVlc == null)
            {
                try { created = new LibVlcAudio("--aout=dummy"); }
                catch (Exception ex)
                {
                    if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CI") == "true") throw;
                    Assert.Skip("libvlc is not installed: " + ex.Message);
                }
            }
            var root = Directory.CreateTempSubdirectory("ccp-no-ffmpeg-").FullName;
            var path = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", root);
                await AvaloniaTestDispatcher.RunAsync(async () =>
                {
                    if (Application.Current is null)
                        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                    var clip = Path.Combine(root, "clip.mp4");
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "thumbnail-160x90.mp4"), clip);
                    var bytes = await VideoThumbnail.ReadAsync(clip);
                    Assert.NotNull(bytes);
                    using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
                    Assert.NotNull(bitmap);
                    Assert.Equal(100, bitmap.Width);
                    Assert.Equal(56, bitmap.Height);
                    var pixel = bitmap.GetPixel(50, 28);
                    Assert.True(pixel.Red > 200 && pixel.Green < 40 && pixel.Blue < 40);
                    File.Delete(clip);
                    Assert.False(File.Exists(clip));
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", path);
                Directory.Delete(root, true);
            }
        }
        finally
        {
            if (created != null)
            {
                typeof(LibVlcAudio).GetProperty("Shared", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, previousVlc);
                created.Vlc.Dispose();
            }
        }
    }
}
