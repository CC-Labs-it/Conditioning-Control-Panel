using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Deeper waveform strip: the head decodes (WPF NAudio), Core owns the bucket rule and
/// the DPK2 peaks cache keyed on path + size + last write. Runs in this assembly's
/// CCP_USERDATA_DIR sandbox, so the cache never touches a real profile.
/// </summary>
public class AudioWaveformCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-wave-" + Guid.NewGuid().ToString("N"));
    private readonly string _savedTag = AudioWaveformCache.DecoderCacheTag;
    private readonly Func<string, AudioWaveformResult>? _savedDecoder = AudioWaveformCache.Decoder;

    public AudioWaveformCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        AudioWaveformCache.Decoder = _savedDecoder;
        AudioWaveformCache.DecoderCacheTag = _savedTag;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Audio(string name, string content = "not really audio")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A reader that hands out <paramref name="samples"/> in buffer-sized chunks.</summary>
    private static Func<float[], int> Reader(float[] samples)
    {
        int pos = 0;
        return buf =>
        {
            int n = Math.Min(buf.Length, samples.Length - pos);
            Array.Copy(samples, pos, buf, 0, n);
            pos += n;
            return n;
        };
    }

    [Theory]
    [InlineData(1, 64)]        // short clip floors at 64
    [InlineData(100, 600)]     // 6 a second
    [InlineData(10_000, 4096)] // long file caps at 4096
    public void BucketPeaks_counts_six_a_second_clamped(double seconds, int expected)
    {
        var result = AudioWaveformCache.BucketPeaks(1, 10, seconds, _ => 0);
        Assert.Equal(expected, result.Peaks.Length);
        Assert.Equal(seconds, result.DurationSeconds);
    }

    [Fact]
    public void BucketPeaks_keeps_the_loudest_sample_of_any_channel_per_bucket()
    {
        // 12 s at 6 Hz stereo: 72 buckets of exactly one frame each.
        const int buckets = 72;
        var samples = new float[buckets * 2];
        for (int i = 0; i < buckets; i++)
        {
            samples[i * 2] = i % 2 == 0 ? -0.5f : 0.1f; // left: negative peaks count by magnitude
            samples[i * 2 + 1] = i % 2 == 0 ? 0.2f : 0.9f;
        }
        var result = AudioWaveformCache.BucketPeaks(2, 6, 12, Reader(samples));

        Assert.Equal(buckets, result.Peaks.Length);
        Assert.Equal(0.5f, result.Peaks[0]);
        Assert.Equal(0.9f, result.Peaks[1]);
        Assert.Equal(0.5f, result.Peaks[70]);
        Assert.Equal(0.9f, result.Peaks[71]);
    }

    [Fact]
    public void BucketPeaks_short_stream_fills_the_tail_with_the_last_max()
    {
        var result = AudioWaveformCache.BucketPeaks(1, 6, 12, Reader(new[] { 0.3f, 0.7f, 0.4f }));
        Assert.Equal(0.3f, result.Peaks[0]);
        Assert.Equal(0.7f, result.Peaks[1]);
        Assert.Equal(0.4f, result.Peaks[2]);
        Assert.Equal(0f, result.Peaks[71]); // nothing pending after the last full bucket
    }

    [Fact]
    public void BucketPeaks_unknown_duration_is_a_flat_strip()
    {
        var result = AudioWaveformCache.BucketPeaks(2, 44100, 0, _ => throw new InvalidOperationException("must not read"));
        Assert.Equal(64, result.Peaks.Length);
        Assert.All(result.Peaks, p => Assert.Equal(0f, p));
    }

    [Fact]
    public void Load_decodes_once_then_serves_the_disk_cache()
    {
        var path = Audio("song.mp3");
        int calls = 0;
        AudioWaveformCache.Decoder = p =>
        {
            calls++;
            return new AudioWaveformResult { Peaks = new[] { 0.25f, 0.75f }, DurationSeconds = 12.5 };
        };

        var first = AudioWaveformCache.Load(path);
        var second = AudioWaveformCache.Load(path);

        Assert.Equal(1, calls);
        Assert.Equal(new[] { 0.25f, 0.75f }, second.Peaks);
        Assert.Equal(12.5, second.DurationSeconds);
        Assert.Equal(first.Peaks, second.Peaks);
        AudioWaveformCache.DecoderCacheTag = "|source-pcm16-v1";
        AudioWaveformCache.Load(path);
        Assert.Equal(2, calls);
        Assert.StartsWith(Path.Combine(CorePaths.UserData, "deeper-cache"), AudioWaveformCache.CacheFolder);
    }

    [Fact]
    public void Load_redecodes_when_the_file_changes()
    {
        var path = Audio("edit.mp3", "v1");
        int calls = 0;
        AudioWaveformCache.Decoder = _ => new AudioWaveformResult { Peaks = new[] { (float)++calls }, DurationSeconds = 1 };

        AudioWaveformCache.Load(path);
        File.WriteAllText(path, "version two, longer");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var after = AudioWaveformCache.Load(path);

        Assert.Equal(2, calls);
        Assert.Equal(2f, after.Peaks[0]);
    }

    [Fact]
    public void Load_ignores_a_cache_file_with_the_wrong_magic()
    {
        var path = Audio("corrupt.mp3");
        int calls = 0;
        AudioWaveformCache.Decoder = _ => { calls++; return new AudioWaveformResult { Peaks = new[] { 0.5f }, DurationSeconds = 2 }; };
        AudioWaveformCache.Load(path);
        foreach (var f in Directory.GetFiles(AudioWaveformCache.CacheFolder, "*.peaks"))
            File.WriteAllText(f, "XXXXgarbage");

        var again = AudioWaveformCache.Load(path);

        Assert.Equal(2, calls);
        Assert.Equal(0.5f, again.Peaks[0]);
    }

    [Fact]
    public void Load_missing_file_or_decoder_throws_for_the_callers_flat_fallback()
    {
        AudioWaveformCache.Decoder = null;
        Assert.Throws<FileNotFoundException>(() => AudioWaveformCache.Load(Path.Combine(_dir, "gone.mp3")));
        Assert.Throws<InvalidOperationException>(() => AudioWaveformCache.Load(Audio("nodecoder.mp3")));
    }
    // WPF MediaDurationProbe.cs:37: source dimensions survive the WAV adapter.
    [Fact]
    public void DecodeWave_buckets_the_mono_16k_pcm()
    {
        using var wav = Wave(1, 16000, i => i == 5 * 2667 + 10 ? (short)-24576 : (short)0);
        var result = AudioWaveformCache.FromPcm16Wave(wav);
        Assert.Equal(20, result.DurationSeconds, 3);
        Assert.Equal(120, result.Peaks.Length);
        Assert.Equal(0.75f, result.Peaks[5]);
        Assert.Equal(0f, result.Peaks[4]); Assert.Equal(0f, result.Peaks[6]);
    }

    [Theory]
    [InlineData(2, 16000)] // opposite-phase stereo must never be averaged
    [InlineData(1, 48000)] // 12 kHz, above the old decoder's 8 kHz Nyquist limit
    public void DecodeWave_preserves_stereo_and_high_frequency_peaks(int channels, int rate)
    {
        using var wav = Wave(channels, rate, i => channels == 2
            ? (short)(i % 2 == 0 ? 24576 : -24576)
            : (short)(i % 4 == 1 ? 24576 : i % 4 == 3 ? -24576 : 0));
        var result = AudioWaveformCache.FromPcm16Wave(wav);
        Assert.Equal(20, result.DurationSeconds, 3);
        Assert.Equal(120, result.Peaks.Length);
        Assert.All(result.Peaks, p => Assert.Equal(0.75f, p));
    }

    private static MemoryStream Wave(int channels, int rate, Func<int, short> sample)
    {
        var wav = new MemoryStream();
        using var w = new BinaryWriter(wav, System.Text.Encoding.UTF8, leaveOpen: true);
        int bytes = channels * rate * 20 * 2;
        w.Write(0x46464952); w.Write(36 + bytes); w.Write(0x45564157);
        w.Write(0x20746d66); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(0x61746164); w.Write(bytes);
        for (int i = 0; i < bytes / 2; i++) w.Write(sample(i));
        wav.Position = 0;
        return wav;
    }

}
