using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;
using LibVLCSharp.Shared;

namespace CCP.Avalonia.Tests;

public sealed class DeeperLocalVideoFrameTests
{
    [Theory]
    [InlineData("mkv")]
    [InlineData("avi")]
    public async Task InitialFrameAndPausedSeekDecode(string extension)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-editor-video-").FullName;
        var path = Path.Combine(dir, "clip." + extension);
        try
        {
            try
            {
                using var ff = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-v error -f lavfi -i testsrc2=d=10:s=160x120:r=10 -f lavfi -i sine=frequency=440:duration=10 -c:v mpeg4 -c:a pcm_s16le -shortest -y \"{path}\"") { UseShellExecute = false })!;
                ff.WaitForExit();
                if (ff.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed");
            }
            catch (Exception ex) { Assert.Skip("ffmpeg unavailable: " + ex.Message); }
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception ex) { Assert.Skip("libvlc unavailable: " + ex.Message); }

            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var image = new Image();
                using var video = await DeeperLocalAudio.OpenVideo(path, image);
                Assert.NotNull(video);
                for (var i = 0; i < 100 && image.Source is null; i++) await Task.Delay(50);
                Assert.IsType<WriteableBitmap>(image.Source);
                await Task.Delay(400);
                var player = PlayerOf(video);
                Assert.Equal(-1, player.AudioTrack); // no audio track is ever active during priming

                var first = Hash((WriteableBitmap)image.Source!);
                var position = video.PositionSeconds;
                await Task.Delay(400);
                Assert.Equal(first, Hash((WriteableBitmap)image.Source!));
                Assert.InRange(Math.Abs(video.PositionSeconds - position), 0, 0.15);

                video.PositionSeconds = 5;
                for (var i = 0; i < 100 && Hash((WriteableBitmap)image.Source!) == first; i++) await Task.Delay(50);
                Assert.NotEqual(first, Hash((WriteableBitmap)image.Source!));
                Assert.InRange(video.PositionSeconds, 4.5, 5.5);
                var seekFrame = Hash((WriteableBitmap)image.Source!);
                await Task.Delay(400);
                Assert.Equal(seekFrame, Hash((WriteableBitmap)image.Source!));
                video.Play();
                for (var i = 0; i < 100 && player.AudioTrack < 0; i++) await Task.Delay(50);
                Assert.True(player.AudioTrack >= 0); // explicit Play switches to normal media
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task AudioOnlyVideoFileRemainsSilentUntilExplicitPlay()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-editor-audio-only-").FullName;
        var path = Path.Combine(dir, "audio-only.mkv");
        try
        {
            try
            {
                using var ff = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-v error -f lavfi -i sine=frequency=440:duration=8 -c:a pcm_s16le -y \"{path}\"") { UseShellExecute = false })!;
                ff.WaitForExit();
                if (ff.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed");
            }
            catch (Exception ex) { Assert.Skip("ffmpeg unavailable: " + ex.Message); }
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception ex) { Assert.Skip("libvlc unavailable: " + ex.Message); }
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var image = new Image();
                using var video = await DeeperLocalAudio.OpenVideo(path, image);
                Assert.NotNull(video);
                var player = PlayerOf(video);
                await Task.Delay(500);
                Assert.Equal(-1, player.AudioTrack);
                await Task.Delay(2200);
                Assert.Null(image.Source);
                Assert.NotEqual(VLCState.Playing, player.State);
                Assert.Equal(-1, player.AudioTrack);
                video.Play();
                for (var i = 0; i < 100 && player.AudioTrack < 0; i++) await Task.Delay(50);
                Assert.True(player.AudioTrack >= 0);
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    private static MediaPlayer PlayerOf(IDeeperLocalAudio video)
    {
        var engine = typeof(DeeperLocalAudio.Transport).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(DeeperLocalAudio.IEngine)).GetValue(video)!;
        return (MediaPlayer)engine.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(MediaPlayer)).GetValue(engine)!;
    }

    private static int Hash(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }
}
