using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Glassroad.SongWords;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>RACE YOUR OWN SONG (Glassroad solo): model pins and chooser, the heard chart, the model installer.</summary>
public class SongWordsTests
{
    [Fact]
    public void Pins_are_upstream_whisper_cpp_files_with_a_size_and_a_hash()
    {
        foreach (var pin in new[] { WhisperModelPins.Small, WhisperModelPins.Medium })
        {
            Assert.StartsWith("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-", pin.Url);
            Assert.EndsWith(pin.FileName, pin.Url);
            Assert.Matches("^[0-9a-f]{64}$", pin.Sha256);
            Assert.True(pin.Bytes > 100L << 20);
        }
        Assert.Equal("465 MB", WhisperModelPins.Small.SizeLabel);
        Assert.Equal("1.4 GB", WhisperModelPins.Medium.SizeLabel);
    }

    [Fact]
    public void The_machine_picks_the_model_and_the_thread_count()
    {
        Assert.Same(WhisperModelPins.Medium, WhisperModelPins.Choose(24, 32L << 30));
        Assert.Same(WhisperModelPins.Medium, WhisperModelPins.Choose(12, 8L << 30));
        Assert.Same(WhisperModelPins.Small, WhisperModelPins.Choose(8, 32L << 30));
        Assert.Same(WhisperModelPins.Small, WhisperModelPins.Choose(16, 4L << 30));
        Assert.Equal(8, WhisperModelPins.Threads(24));
        Assert.Equal(6, WhisperModelPins.Threads(13));
        Assert.Equal(2, WhisperModelPins.Threads(4));
        Assert.Equal(2, WhisperModelPins.Threads(1));
    }

    [Fact]
    public void Song_ids_follow_the_page_shape_and_change_with_the_file()
    {
        var t = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var a = SongChartBuilder.SongId(@"C:\Music\Song.mp3", 1000, t);
        Assert.Matches("^u[0-9a-f]{16}$", a);
        Assert.Equal(a, SongChartBuilder.SongId(@"c:\music\song.MP3", 1000, t));
        Assert.NotEqual(a, SongChartBuilder.SongId(@"C:\Music\Song.mp3", 1001, t));
        Assert.NotEqual(a, SongChartBuilder.SongId(@"C:\Music\Song.mp3", 1000, t.AddSeconds(1)));
        Assert.Equal("my song", SongChartBuilder.Label("my_song.mp3"));
        Assert.Equal("My song", SongChartBuilder.Label("My song"));
        Assert.True(SongChartBuilder.Label(new string('x', 80) + ".flac").Length <= SongChartBuilder.MaxLabel);
        Assert.Equal("My song", SongChartBuilder.Label(".mp3"));
    }

    [Fact]
    public void The_heard_chart_drops_noise_marks_confident_longer_words_and_keeps_time_in_order()
    {
        var words = new List<SongWord>
        {
            new("[MUSIC]", 0.0, 1.0, 0.9, 0),
            new("Hold", 1.0, 1.3, 0.9, 0),
            new("me", 1.3, 1.4, 0.9, 0),
            new("closer", 1.2, 1.9, 0.7, 0),      // starts before the word before it: pushed to keep order
            new("mumble", 3.0, 3.2, 0.2, 1),      // too unsure to paint
            new("river", 4.0, 4.4, 0.5, 1),       // sure enough for the road, not for a bubble
            new("sunlight", 70.0, 70.5, 0.95, 2), // past the end: clamped
        };
        var chart = SongChartBuilder.Build("u1", "My song", 60, "audio.mp3", words, "medium");
        Assert.Equal("u1", (string?)chart["id"]);
        Assert.True((bool)chart["userSong"]!);
        Assert.Equal(SongChartBuilder.PlaceholderBpm, (int)chart["bpm"]!);
        Assert.Equal("medium", (string?)chart["model"]);
        Assert.Single((JArray)chart["sections"]!);
        var ws = (JArray)chart["words"]!;
        Assert.Equal(new[] { "Hold", "me", "closer", "river", "sunlight" }, ws.Select(w => (string?)w["w"]).ToArray());
        Assert.Equal(new[] { true, false, true, false, true }, ws.Select(w => (bool)w["bubble"]!).ToArray());
        var starts = ws.Select(w => (double)w["startS"]!).ToArray();
        Assert.True(starts.Zip(starts.Skip(1), (p, n) => n >= p).All(x => x), "monotone");
        Assert.Equal(1.3, starts[2]);
        Assert.Equal(59.95, starts[4], 3);
        Assert.Equal(60, (double)ws[4]["endS"]!, 3);
        Assert.Equal(new[] { 0, 0, 0, 1, 2 }, ws.Select(w => (int)w["line"]!).ToArray());
        var lines = ((JArray)chart["lines"]!).Select(l => (string?)l).ToArray();
        Assert.Equal(new[] { "Hold me closer", "river", "sunlight" }, lines);
        Assert.All(ws, w => Assert.False((bool)w["gate"]!));
    }

    [Fact]
    public void Noise_words_are_brackets_and_symbols()
    {
        Assert.True(SongChartBuilder.IsNoise("[MUSIC]"));
        Assert.True(SongChartBuilder.IsNoise("(laughs)"));
        Assert.True(SongChartBuilder.IsNoise("..."));
        Assert.True(SongChartBuilder.IsNoise(""));
        Assert.False(SongChartBuilder.IsNoise("don't"));
        Assert.False(SongChartBuilder.IsNoise("Hold,"));
        Assert.Equal(4, SongChartBuilder.Letters("Hold,"));
    }

    private static WhisperModelPin PinFor(byte[] bytes, string? sha = null)
        => new("t", "ggml-t.bin", "https://example.invalid/ggml-t.bin", bytes.Length, sha ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static Func<CancellationToken, Task<Stream>> Open(byte[] bytes) => _ => Task.FromResult<Stream>(new MemoryStream(bytes));

    [Fact]
    public async Task The_installer_refuses_a_short_long_or_tampered_download_and_leaves_nothing_behind()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-songwords-" + Guid.NewGuid().ToString("N"));
        try
        {
            var good = new byte[5000];
            new Random(7).NextBytes(good);
            var pin = PinFor(good);
            var target = WhisperModelPins.PathFor(dir, pin);

            Assert.Equal(WhisperInstallResult.SizeMismatch, await new WhisperModelInstaller(dir, pin, Open(good.Take(4000).ToArray())).InstallAsync(null, CancellationToken.None));
            Assert.Equal(WhisperInstallResult.SizeMismatch, await new WhisperModelInstaller(dir, pin, Open(good.Concat(new byte[1]).ToArray())).InstallAsync(null, CancellationToken.None));
            var tampered = (byte[])good.Clone(); tampered[123] ^= 1;
            Assert.Equal(WhisperInstallResult.HashMismatch, await new WhisperModelInstaller(dir, pin, Open(tampered)).InstallAsync(null, CancellationToken.None));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.Equal(WhisperInstallResult.Cancelled, await new WhisperModelInstaller(dir, pin, _ => throw new OperationCanceledException()).InstallAsync(null, cts.Token));
            }
            Assert.False(File.Exists(target));
            Assert.False(File.Exists(target + ".part"));
            Assert.False(WhisperModelPins.IsInstalled(dir, pin));

            var seen = new List<double>();
            var progress = new Progress<double>(seen.Add);
            Assert.Equal(WhisperInstallResult.Installed, await new WhisperModelInstaller(dir, pin, Open(good)).InstallAsync(progress, CancellationToken.None));
            Assert.True(File.Exists(target));
            Assert.False(File.Exists(target + ".part"));
            Assert.Equal(good, await File.ReadAllBytesAsync(target));
            Assert.True(WhisperModelPins.IsInstalled(dir, pin));
            // installed already: no download, no stream opened
            Assert.Equal(WhisperInstallResult.Installed, await new WhisperModelInstaller(dir, pin, _ => throw new InvalidOperationException("opened")).InstallAsync(null, CancellationToken.None));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}
