using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SmtcWindowsSessionTests
{
    [Fact]
    public async Task NativeWatcherReadsControlledSession()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        await ReadControlledSessionAsync();
    }

    // This uses a real SMTC publisher and the watcher's default native acquisition and read.
    [SupportedOSPlatform("windows10.0.17763")]
    private static async Task ReadControlledSessionAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ccp-smtc-{Guid.NewGuid():N}.wav");
        string title = $"CCP SMTC test {Guid.NewGuid():N}";
        const string artist = "CCP SMTC test artist";
        var expectedPosition = TimeSpan.FromSeconds(42);
        try
        {
            // A silent PCM source creates an audio playback session without an external fixture.
            using (var file = File.Create(path))
            using (var writer = new BinaryWriter(file))
            {
                const int sampleRate = 8000, samples = sampleRate * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate);
                writer.Write((short)1); writer.Write((short)8);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples);
                var silence = new byte[samples];
                Array.Fill(silence, (byte)128);
                writer.Write(silence);
            }

            using var player = new MediaPlayer { IsLoopingEnabled = true, IsMuted = true };
            player.CommandManager.IsEnabled = false;
            var controls = player.SystemMediaTransportControls;
            controls.IsEnabled = true;
            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            var playing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackSession.PlaybackStateChanged += (sender, _) =>
            {
                if (sender.PlaybackState == MediaPlaybackState.Playing) playing.TrySetResult(true);
            };
            player.MediaFailed += (_, error) => playing.TrySetException(new InvalidOperationException(error.ErrorMessage));
            player.Source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(path));
            player.Play();
            await playing.Task.WaitAsync(TimeSpan.FromSeconds(20));

            var updater = controls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = title;
            updater.MusicProperties.Artist = artist;
            updater.Update();
            var timeline = new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero, MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = TimeSpan.FromMinutes(3), EndTime = TimeSpan.FromMinutes(3),
                Position = expectedPosition
            };
            controls.UpdateTimelineProperties(timeline);
            controls.PlaybackStatus = MediaPlaybackStatus.Paused;

            using var watcher = new SmtcMediaWatcher();
            watcher.Start();
            await watcher.StartCompletion.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(watcher.IsAvailable, "SMTC manager acquisition failed");

            // Windows publishes the session asynchronously. Do not accept another player's data.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            do
            {
                await watcher.PollAsync();
                var sample = watcher.Current;
                if (sample?.Title == title && sample.Artist == artist &&
                    sample.PlaybackState == "Paused" && sample.Position == expectedPosition) break;
                await Task.Delay(100);
            } while (DateTime.UtcNow < deadline);
            Assert.NotNull(watcher.Current);
            Assert.Equal(title, watcher.Current.Title);
            Assert.Equal(artist, watcher.Current.Artist);
            Assert.Equal("Paused", watcher.Current.PlaybackState);
            Assert.Equal(expectedPosition, watcher.Current.Position);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
