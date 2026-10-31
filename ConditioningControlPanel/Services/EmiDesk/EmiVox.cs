using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BLIPESE, the seam. The bubble talks to this and never has to know whether there is an audio
/// stack under it (and a test can hand it a recorder instead).
/// </summary>
public interface IEmiVox : IDisposable
{
    /// <summary>Babble one landed line. False when nothing was played.</summary>
    bool Speak(string? text, string? mood);

    /// <summary>One typing tick, for a <c>.</c> / <c>..</c> / <c>...</c> bubble frame.</summary>
    bool Tick();

    /// <summary>The bubble cleared: cut whatever is still sounding.</summary>
    void Stop();
}

/// <summary>
/// THE SOUND SHE MAKES WHILE A LINE IS ON SCREEN, ported from the Arcademy's
/// <c>Resources/web/arcademy/emi/vox.js</c> plus the two recipes its cues resolve to in
/// <c>Resources/web/arcademy/shell/audio.js</c> (<c>emi_blip</c> and <c>emi_tick</c>).
///
/// <para>THE PORT ANSWERS ONE QUESTION FIRST: the web voice is SYNTHESIS, not files. There is no
/// blip.wav anywhere in the Arcademy. <c>vox.js</c> computes a score of {atMs, pitch, gain} and
/// WebAudio builds every blip out of a triangle oscillator gliding 760 Hz down to 680 Hz across
/// 56 ms. So there was nothing to copy across and the synthesis had to come with it: everything
/// below the score is that oscillator, rendered offline.</para>
///
/// <para>ONE BUFFER PER LINE, NEVER ONE DEVICE PER BLIP. A thirteen-blip burst through thirteen
/// <c>PlayOneShot</c> calls is thirteen <c>WaveOutEvent</c>s inside 1.4 s, which is the exact
/// pattern the ChaosSfx history warns about. The whole burst is mixed into ONE mono 44.1 kHz
/// buffer with the gaps baked in as silence, written to a WAV and played as a single clip. The
/// score is deterministic (the seed IS the line text), so the WAV is cached on disk by content
/// hash and a line she has said before costs a file-exists check.</para>
///
/// <para>THE PACE IS THE IDENTITY. Every number in <see cref="VoxDials"/> is the web's, unchanged,
/// including the owner's 2026-08-24 transpose (<c>BasePitch</c> 0.82). Retune here and the desktop
/// stops sounding like the campus.</para>
///
/// <para>WHAT SILENCES HER: master volume at zero, the audio breaker, and any hold or panic
/// silence in <see cref="EmiLineEngine"/>. What does NOT silence her is the avatar mute arbiter.
/// Blips are a texture rather than a second voice, and there is no desk-sound setting to hang
/// them off, so there is no new setting: they are on.</para>
/// </summary>
public sealed class EmiVox : IEmiVox
{
    /// <summary>Compatibility API; voice values and synthesis live in Core.</summary>
    public static class VoxDials
    {
        public const double Level = EmiVoxScore.VoxDials.Level;
        public const double GapSylMs = EmiVoxScore.VoxDials.GapSylMs;
        public const double GapWordMs = EmiVoxScore.VoxDials.GapWordMs;
        public const double GapSentMs = EmiVoxScore.VoxDials.GapSentMs;
        public const double TailRestMs = EmiVoxScore.VoxDials.TailRestMs;
        public const double JitterGapMs = EmiVoxScore.VoxDials.JitterGapMs;
        public const int MaxBlips = EmiVoxScore.VoxDials.MaxBlips;
        public const double BurstMaxMs = EmiVoxScore.VoxDials.BurstMaxMs;
        public const double BasePitch = EmiVoxScore.VoxDials.BasePitch;
        public const double JitterSemi = EmiVoxScore.VoxDials.JitterSemi;
        public const double QuestionRise = EmiVoxScore.VoxDials.QuestionRise;
        public const int QuestionTail = EmiVoxScore.VoxDials.QuestionTail;
        public const double BangSemi = EmiVoxScore.VoxDials.BangSemi;
        public const double BangGain = EmiVoxScore.VoxDials.BangGain;
        public const double BangGap = EmiVoxScore.VoxDials.BangGap;
        public const double SadTailSemi = EmiVoxScore.VoxDials.SadTailSemi;
        public const double TailGain = EmiVoxScore.VoxDials.TailGain;
        public const double PitchMin = EmiVoxScore.VoxDials.PitchMin;
        public const double PitchMax = EmiVoxScore.VoxDials.PitchMax;
        public const double TickLevel = 0.22;
    }

    public static readonly IReadOnlyDictionary<string, EmiVoxScore.Mood> Moods = EmiVoxScore.Moods;
    internal static Func<double> MakeRng(string? seed) => EmiVoxScore.MakeRng(seed);
    public static int Syllables(string? word) => EmiVoxScore.Syllables(word);
    public static IReadOnlyList<EmiVoxScore.VoxBlip> MakeScore(string? text, string? mood) => EmiVoxScore.MakeScore(text, mood);
    internal static float[] RenderBurst(IReadOnlyList<EmiVoxScore.VoxBlip> score) => EmiVoxScore.RenderBurst(score);
    internal static byte[] WriteWav(float[] samples) => EmiVoxScore.WriteWav(samples);

    private const double TickF0 = 336.0, TickF1 = 322.0, TickMs = 30.0, TickGain = 0.16, TickAttack = 0.35;
    /// <summary>The WPF typing tick uses the shared oscillator; the tick recipe stays here.</summary>
    internal static float[] RenderTick()
    {
        var buf = new float[(int)((TickMs + 20.0) / 1000.0 * EmiVoxScore.SampleRate) + 8];
        EmiVoxScore.MixTone(buf, 0, TickF0, TickF1, TickMs, TickGain, TickAttack, VoxDials.TickLevel, 1.0);
        return buf;
    }

    // ---------------------------------------------------------------- the instance

    private static string ClipDir => Path.Combine(CorePaths.UserData, "emi", "vox");

    /// <summary>How many cached line clips to keep before the oldest are swept.</summary>
    private const int CacheCap = 96;

    private readonly object _gate = new();
    private AudioPlaybackHandle? _live;
    private bool _dead;
    private string? _tickPath;

    /// <summary>Diagnostics only: how many lines have gone out.</summary>
    public int Spoke { get; private set; }

    /// <summary>Diagnostics only: how many typing ticks have gone out.</summary>
    public int Ticks { get; private set; }

    /// <summary>
    /// True when a sound may leave this object at all: the app is not muted, the audio breaker is
    /// shut, and nothing safety-shaped is holding her quiet.
    /// </summary>
    private static bool Audible(out float volume)
    {
        volume = 0f;
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return false;

            int master = App.Settings?.Current?.MasterVolume ?? 0;
            if (master <= 0) return false;

            // SAFETY IS SILENCE. A hold or the panic tail means she is deliberately quiet, and a
            // texture that keeps chirping through one is the feature reading as broken.
            if (EmiLineEngine.Instance.HoldActive) return false;

            volume = (float)Math.Clamp(master / 100.0, 0, 1);
            return volume > 0f;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox audibility probe failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Speak(string? text, string? mood)
    {
        if (_dead) return false;
        var line = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (!Audible(out var volume)) return false;

        try
        {
            var score = MakeScore(line, mood);
            if (score.Count == 0) return false;

            var path = EnsureClip("line", line + "|" + (mood ?? "idle"), () => RenderBurst(score));
            if (path == null) return false;

            Stop();                                   // ONE VOICE: never two at once
            var handle = App.Audio?.PlayOneShot(path, volume, "emi-vox");
            lock (_gate) _live = handle;
            Spoke++;
            return handle != null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox speak failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Tick()
    {
        if (_dead) return false;
        if (!Audible(out var volume)) return false;
        try
        {
            _tickPath ??= EnsureClip("tick", "v1", RenderTick);
            if (_tickPath == null) return false;

            // A tick is 30 ms and lands between bursts, so it does NOT take the live slot: cutting
            // a burst on a dot frame would be the typewriter this voice is deliberately not.
            App.Audio?.PlayOneShot(_tickPath, volume, "emi-vox-tick");
            Ticks++;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox tick failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        AudioPlaybackHandle? live;
        lock (_gate) { live = _live; _live = null; }
        if (live == null) return;
        try { live.Stop(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] vox stop failed"); }
    }

    /// <summary>
    /// The WAV for this key, rendered once. The score is deterministic, so the file is too: a line
    /// she has said before is a file-exists check and nothing else.
    /// </summary>
    private static string? EnsureClip(string kind, string key, Func<float[]> render)
    {
        try
        {
            var dir = ClipDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, kind + "-" + KeyHash(key) + ".wav");

            var info = new FileInfo(path);
            if (info.Exists && info.Length > 44) return path;

            var samples = render();
            if (samples.Length == 0) return null;
            File.WriteAllBytes(path, WriteWav(samples));
            SweepCache(dir);
            return path;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox clip render failed");
            return null;
        }
    }

    /// <summary>FNV-1a again, 64-bit this time. A file name, not a security boundary.</summary>
    private static string KeyHash(string? key)
    {
        ulong h = 14695981039346656037ul;
        foreach (var b in Encoding.UTF8.GetBytes(key ?? string.Empty))
        {
            h ^= b;
            h = unchecked(h * 1099511628211ul);
        }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>Keep the cache bounded: her vocabulary is finite, but a machine's uptime is not.</summary>
    private static void SweepCache(string dir)
    {
        try
        {
            var files = new DirectoryInfo(dir).GetFiles("line-*.wav");
            if (files.Length <= CacheCap) return;
            foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - CacheCap))
            {
                try { f.Delete(); } catch { /* a cached blip is never worth an exception */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] vox cache sweep failed"); }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_dead) return;
        _dead = true;
        Stop();
    }
}
