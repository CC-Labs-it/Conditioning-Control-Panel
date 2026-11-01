using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class VideoThumbnailVlcTests
{
    private static bool IsCi(string? ci) => ci == "1" || string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase);

    private static void RequireWindowsBundle(string? ci, bool windows, string root)
    {
        if (!windows || !IsCi(ci)) return;
        var bundle = Path.Combine(root, "libvlc");
        static bool HasRuntime(string dir) => File.Exists(Path.Combine(dir, "libvlc.dll")) && File.Exists(Path.Combine(dir, "libvlccore.dll"));
        if (!HasRuntime(bundle) && !HasRuntime(Path.Combine(bundle, Environment.Is64BitProcess ? "win-x64" : "win-x86")))
            throw new FileNotFoundException("Bundled LibVLC runtime is missing.", bundle);
    }

    private static LibVlcAudio StartVlc(Func<LibVlcAudio> factory, string? ci, bool windows, bool linux)
    {
        try { return factory(); }
        catch (Exception ex)
        {
            if ((windows || linux) && IsCi(ci)) throw;
            Assert.Skip("libvlc is not installed: " + ex.Message);
            throw;
        }
    }

    [Theory]
    [InlineData(true, false, "true")]
    [InlineData(true, false, "1")]
    [InlineData(false, true, "true")]
    public void CiDoesNotSkipVlcInitializationFailure(bool windows, bool linux, string ci)
    {
        var error = new InvalidOperationException("broken runtime");
        Exception? caught = null;
        try { StartVlc(() => throw error, ci, windows, linux); }
        catch (Exception ex) { caught = ex; } // xUnit otherwise treats a leaked Skip as a skipped test
        Assert.Same(error, caught);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsCiRequiresTheBundledRuntime(bool architectureFolder)
    {
        var root = Directory.CreateTempSubdirectory("ccp-vlc-bundle-").FullName;
        try
        {
            Assert.Throws<FileNotFoundException>(() => RequireWindowsBundle("true", true, root));
            var bundle = Path.Combine(root, "libvlc");
            if (architectureFolder) bundle = Path.Combine(bundle, Environment.Is64BitProcess ? "win-x64" : "win-x86");
            Directory.CreateDirectory(bundle);
            File.WriteAllBytes(Path.Combine(bundle, "libvlc.dll"), Array.Empty<byte>());
            Assert.Throws<FileNotFoundException>(() => RequireWindowsBundle("true", true, root));
            File.WriteAllBytes(Path.Combine(bundle, "libvlccore.dll"), Array.Empty<byte>());
            RequireWindowsBundle("true", true, root);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public Task CleanupWaitsForTheUiThread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var entered = new ManualResetEventSlim();
        using var freed = new ManualResetEventSlim();
        bool ranOnUi = false;
        var cleanup = Task.Run(async () =>
        {
            entered.Set();
            await VideoThumbnail.FreeOnUiAsync(() => { ranOnUi = Dispatcher.UIThread.CheckAccess(); freed.Set(); });
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        // Hold the dispatcher as if a blit were still encoding its bitmap.
        var freedWhileUiBusy = freed.Wait(TimeSpan.FromMilliseconds(300));
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(freedWhileUiBusy);
        Assert.True(ranOnUi);
    });

    [Fact]
    public async Task RealThumbnailDoesNotFreeItsSinkWhileTheUiFrameIsBlocked()
    {
        RequireWindowsBundle(Environment.GetEnvironmentVariable("CI"), OperatingSystem.IsWindows(), AppContext.BaseDirectory);
        var previousVlc = LibVlcAudio.Shared;
        LibVlcAudio? created = null;
        using var inFrame = new ManualResetEventSlim();
        using var releaseFrame = new ManualResetEventSlim();
        using var freeing = new ManualResetEventSlim();
        Task<byte[]?>? decoding = null;
        bool freedOnUi = false;
        try
        {
            if (previousVlc == null)
                created = StartVlc(() => new LibVlcAudio("--aout=dummy"), Environment.GetEnvironmentVariable("CI"),
                    OperatingSystem.IsWindows(), OperatingSystem.IsLinux());
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                return Task.CompletedTask;
            });
            var clip = Path.Combine(AppContext.BaseDirectory, "Assets", "thumbnail-160x90.mp4");
            decoding = Task.Run(() => VideoThumbnail.ReadAsync(clip,
                () => { inFrame.Set(); releaseFrame.Wait(TimeSpan.FromSeconds(30)); },
                () => { freedOnUi = Dispatcher.UIThread.CheckAccess(); freeing.Set(); }));
            var entered = inFrame.Wait(TimeSpan.FromSeconds(20));
            var freedWhileBlocked = entered && freeing.Wait(TimeSpan.FromSeconds(1));
            releaseFrame.Set();
            var image = await decoding.WaitAsync(TimeSpan.FromSeconds(35));
            Assert.True(entered, "the real thumbnail never delivered a frame");
            Assert.False(freedWhileBlocked);
            Assert.True(freeing.IsSet);
            Assert.True(freedOnUi);
            Assert.NotNull(image);
        }
        finally
        {
            releaseFrame.Set();
            if (decoding != null && !decoding.IsCompleted) await decoding.WaitAsync(TimeSpan.FromSeconds(35));
            if (created != null)
            {
                typeof(LibVlcAudio).GetProperty("Shared", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, previousVlc);
                created.Vlc.Dispose();
            }
        }
    }

    [Fact]
    public async Task DecodesARealVideoWithoutFfmpegOnPath()
    {
        RequireWindowsBundle(Environment.GetEnvironmentVariable("CI"), OperatingSystem.IsWindows(), AppContext.BaseDirectory);
        var previousVlc = LibVlcAudio.Shared;
        LibVlcAudio? created = null;
        try
        {
            if (previousVlc == null)
            {
                created = StartVlc(() => new LibVlcAudio("--aout=dummy"), Environment.GetEnvironmentVariable("CI"),
                    OperatingSystem.IsWindows(), OperatingSystem.IsLinux());
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
                    // The video can end while the UI has not yet run its queued frame blit.
                    using var started = new ManualResetEventSlim();
                    var queued = Task.Run(() => { started.Set(); return VideoThumbnail.ReadAsync(clip); });
                    Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
                    Thread.Sleep(TimeSpan.FromSeconds(4));
                    Assert.NotNull(await queued.WaitAsync(TimeSpan.FromSeconds(20)));
                    File.Delete(clip);
                    Assert.False(File.Exists(clip));

                    // A valid audio-only file has no video frame to send to the sink.
                    var sound = Path.Combine(root, "sound.wav");
                    using (var w = new BinaryWriter(File.Create(sound)))
                    {
                        const int samples = 2205, rate = 22050;
                        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVEfmt "u8); w.Write(16);
                        w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
                        w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(samples * 2); w.Write(new byte[samples * 2]);
                    }
                    var pending = VideoThumbnail.ReadAsync(sound);
                    var finished = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));
                    var noFrame = await pending; // also drain the decoder on the failure path
                    Assert.Same(pending, finished);
                    Assert.Null(noFrame);
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
