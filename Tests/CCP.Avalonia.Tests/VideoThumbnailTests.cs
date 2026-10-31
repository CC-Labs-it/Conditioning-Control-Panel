using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoThumbnailTests
{
    // WPF MainWindow.Assets.cs:921-925: real thumbnail extraction, off-thread and silent.
    [Fact]
    public Task RealAudioClipProducesASilentSmallFrameAndFreesTheDecoder() => Run(async () =>
    {
        var clip = Path.Combine(AppContext.BaseDirectory, "Assets", "help-clip-audio-2s.mp4");
        // Positive control: this fixture and audio callback really detect decoded audio.
        var heard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var media = new Media(LibVlcAudio.Shared!, clip, FromType.FromPath))
        using (var player = new MediaPlayer(LibVlcAudio.Shared!))
        {
            player.SetAudioFormat("S16N", 48000, 1);
            player.SetAudioCallbacks((_, _, count, _) => { if (count > 0) heard.TrySetResult(); }, null, null, null, null);
            media.AddOption(":no-video");
            try { Assert.True(player.Play(media)); await heard.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { await Task.Run(player.Stop); }
        }
        MediaPlayer? decoder = null;
        VlcFrameSink? frames = null;
        var samples = 0;
        var decodedWidth = 0;
        using var thumbnail = await VideoThumbnail.LoadAsync(clip, TimeSpan.FromSeconds(10), (player, sink) =>
        {
            decoder = player;
            frames = sink;
            player.SetAudioFormat("S16N", 48000, 1);
            player.SetAudioCallbacks((_, _, count, _) => Interlocked.Add(ref samples, (int)count), null, null, null, null);
            // Stop precedes Free. Observe the decoder-sized bitmap, before the final copy/resize.
            player.Stopped += (_, _) => decodedWidth = sink.Bitmap?.PixelSize.Width ?? 0;
        });
        Assert.NotNull(thumbnail); // fails if the production decoder always returns null
        Assert.Equal(100, thumbnail.PixelSize.Width);
        Assert.InRange(thumbnail.PixelSize.Height, 1, 100);
        Assert.Equal(100, decodedWidth); // fails if resizing happens only after a full-size UI copy
        Assert.Equal(0, samples);
        Assert.True(frames!.FrameCount > 0);
        Assert.True(frames.Freed);
        Assert.Null(frames.Bitmap);
        Assert.Equal(IntPtr.Zero, decoder!.NativeReference);
    });

    // WPF MainWindow.Assets.cs:940-948: failed thumbnails leave the placeholder and finish loading.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailureAndExpiredDeadlineFreeTheNativePlayerAndBuffer(bool expired) => Run(async () =>
    {
        var invalid = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(invalid, new byte[16]);
            var path = expired ? Path.Combine(AppContext.BaseDirectory, "Assets", "help-clip-audio-2s.mp4") : invalid;
            MediaPlayer? decoder = null;
            VlcFrameSink? frames = null;
            using var thumbnail = await VideoThumbnail.LoadAsync(path,
                expired ? TimeSpan.Zero : TimeSpan.FromMilliseconds(250), (player, sink) => { decoder = player; frames = sink; });
            Assert.Null(thumbnail);
            Assert.NotNull(decoder);
            Assert.True(frames!.Freed);
            Assert.Null(frames.Bitmap);
            Assert.Equal(IntPtr.Zero, decoder.NativeReference);
        }
        finally { File.Delete(invalid); }
    });

    private static Task Run(Func<Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        // Required, not skipped: these tests must actually exercise the production native decoder.
        if (LibVlcAudio.Shared == null) _ = new LibVlcAudio("--aout=dummy");
        await test();
    });
}
