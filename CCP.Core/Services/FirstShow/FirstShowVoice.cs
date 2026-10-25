using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.FirstShow;

/// <summary>WPF EmiVox score and PCM synthesis. Only the first-show burst is imported;
/// ponytail: desk playback, typing ticks and its persistent cache remain head-owned.</summary>
public static class FirstShowVoice
{
    public static class VoxDials
    {
        public const double Level = 0.4;


        public const double GapSylMs = 62.0;
        public const double GapWordMs = 115.0;
        public const double GapSentMs = 160.0;
        public const double TailRestMs = 180.0;
        public const double JitterGapMs = 10.0;
        public const int MaxBlips = 13;
        public const double BurstMaxMs = 1400.0;

        public const double BasePitch = 0.82;
        public const double JitterSemi = 1.2;
        public const double QuestionRise = 3.0;
        public const int QuestionTail = 3;
        public const double BangSemi = 1.0;
        public const double BangGain = 1.25;
        public const double BangGap = 0.85;
        public const double SadTailSemi = -2.0;
        public const double TailGain = 0.8;

        public const double PitchMin = 0.5;
        public const double PitchMax = 2.0;
    }

    public readonly record struct Mood(double Pitch, double Gap, double Gain, double Jitter, double Decline, double Tail);

    public static readonly IReadOnlyDictionary<string, Mood> Moods =
        new Dictionary<string, Mood>(StringComparer.Ordinal)
        {
            ["idle"] = new(1.00, 1.00, 1.00, 1.0, 2.0, 0.0),
            ["celebration"] = new(1.15, 0.85, 1.15, 1.4, -1.5, 0.0),
            ["pet"] = new(1.05, 1.10, 0.80, 0.7, 1.4, 0.0),
            ["smug"] = new(0.95, 1.25, 0.90, 0.9, 2.2, -1.5),
            ["sad"] = new(0.85, 1.30, 0.75, 0.5, 0.6, -2.0),
            ["shock"] = new(1.20, 0.60, 1.10, 1.6, 2.0, 0.0),
        };

    public readonly record struct VoxBlip(int AtMs, double Pitch, double Gain);


    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    private static double SemiToRatio(double s) => Math.Pow(2.0, s / 12.0);

    internal static Func<double> MakeRng(string? seed)
    {
        uint h = 2166136261u;
        foreach (var ch in seed ?? string.Empty)
        {
            h ^= ch;
            h = unchecked(h * 16777619u);
        }
        double hash01 = h / 4294967295.0;
        uint state = (uint)Math.Floor(hash01 * 4294967295.0);
        return () =>
        {
            state = unchecked(state + 0x6D2B79F5u);
            uint t = unchecked((uint)((int)(state ^ (state >> 15)) * (int)(1u | state)));
            t = unchecked((t + (uint)((int)(t ^ (t >> 7)) * (int)(61u | t))) ^ t);
            return (t ^ (t >> 14)) / 4294967296.0;
        };
    }

    public static int Syllables(string? word)
    {
        int groups = 0;
        bool inGroup = false;
        foreach (var raw in word ?? string.Empty)
        {
            char c = char.ToLowerInvariant(raw);
            if (c < 'a' || c > 'z') continue;
            bool vowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (vowel)
            {
                if (!inGroup) { groups++; inGroup = true; }
            }
            else inGroup = false;
        }
        if (groups < 1) return 1;
        return groups > 4 ? 4 : groups;
    }

    private sealed class Word
    {
        public int Syl;
        public bool Ends, Ellipsis, Question, Bang;
    }

    private const string TrailingPunct = ".,!?;:…)\"']";

    private static List<Word> Tokenize(string? text)
    {
        var words = new List<Word>();
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0) return words;

        foreach (var chunk in raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            int cut = chunk.Length;
            while (cut > 0 && TrailingPunct.IndexOf(chunk[cut - 1]) >= 0) cut--;
            var punct = chunk.Substring(cut);
            var body = cut > 0 ? chunk.Substring(0, cut) : chunk;

            bool ellipsis = punct.Contains("...", StringComparison.Ordinal) || punct.Contains('…');
            words.Add(new Word
            {
                Syl = Syllables(body),
                Ends = ellipsis || punct.Contains('?') || punct.Contains('!') || punct.Contains('.'),
                Ellipsis = ellipsis,
                Question = punct.Contains('?'),
                Bang = punct.Contains('!')
            });
        }

        if (words.Count > 0) words[words.Count - 1].Ends = true;
        return words;
    }

    private static bool DropOne(List<Word> words)
    {
        int best = -1;
        for (int i = 0; i < words.Count; i++)
            if (words[i].Syl > 1 && (best < 0 || words[i].Syl > words[best].Syl)) best = i;
        if (best >= 0) { words[best].Syl -= 1; return true; }
        if (words.Count <= 2) return false;

        int cutAt = words.Count / 2;
        var gone = words[cutAt];
        words.RemoveAt(cutAt);

        if (gone.Ends)
        {
            var prev = words[cutAt - 1];
            prev.Ends = true;
            prev.Ellipsis |= gone.Ellipsis;
            prev.Question |= gone.Question;
            prev.Bang |= gone.Bang;
        }
        return true;
    }

    private sealed class Beat
    {
        public double T;
        public int Sentence, Index, Count;
        public bool LastOfWord, Ellipsis, Question;
        public double Pitch, Gain;
    }

    private static List<Beat> Layout(List<Word> words, Mood m, bool bang, string seed)
    {
        var rng = MakeRng("emi-vox|" + seed);
        double gapMul = m.Gap * (bang ? VoxDials.BangGap : 1.0);
        double Jgap(double ms) => Math.Max(16.0, ms * gapMul + (rng() * 2 - 1) * VoxDials.JitterGapMs);

        int s = 0;
        var sentOf = new int[words.Count];
        var counts = new List<int>();
        for (int i = 0; i < words.Count; i++)
        {
            sentOf[i] = s;
            while (counts.Count <= s) counts.Add(0);
            counts[s] += words[i].Syl;
            if (words[i].Ends) s++;
        }

        var blips = new List<Beat>();
        double t = 0;
        var seen = new Dictionary<int, int>();
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (i > 0)
            {
                var prev = words[i - 1];
                double g = VoxDials.GapWordMs;
                if (prev.Ends) g += VoxDials.GapSentMs;
                if (prev.Ellipsis) g += VoxDials.TailRestMs;   // trailing off is a REST, not a rush
                t += Jgap(g);
            }
            for (int k = 0; k < w.Syl; k++)
            {
                if (k > 0) t += Jgap(VoxDials.GapSylMs);
                int si = sentOf[i];
                int j = seen.TryGetValue(si, out var had) ? had : 0;
                seen[si] = j + 1;
                blips.Add(new Beat
                {
                    T = t,
                    Sentence = si,
                    Index = j,
                    Count = counts[si],
                    LastOfWord = k == w.Syl - 1,
                    Ellipsis = w.Ellipsis,
                    Question = w.Question
                });
            }
        }
        if (blips.Count == 0) return blips;

        var asks = new HashSet<int>();
        foreach (var b in blips) if (b.Question) asks.Add(b.Sentence);

        int last = blips.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            var b = blips[i];
            double frac = b.Count > 1 ? b.Index / (double)(b.Count - 1) : 0;
            double semi = 1 - m.Decline * frac;
            bool jitter = true;
            bool lifted = false;

            if (asks.Contains(b.Sentence))
            {
                int k = Math.Min(VoxDials.QuestionTail, b.Count);
                int mm = b.Index - (b.Count - k);
                if (mm >= 0)
                {
                    semi = 1 + (VoxDials.QuestionRise * (mm + 1)) / k;
                    jitter = false;
                    lifted = true;
                }
            }
            if (bang) semi += VoxDials.BangSemi;
            if (b.Ellipsis && b.LastOfWord) { semi += VoxDials.SadTailSemi; jitter = false; }

            if (i == last && !lifted) semi += m.Tail;
            if (jitter) semi += (rng() * 2 - 1) * VoxDials.JitterSemi * m.Jitter;

            double gain = VoxDials.Level * m.Gain * (bang ? VoxDials.BangGain : 1.0);
            if ((b.Ellipsis && b.LastOfWord) || (i == last && !lifted && m.Tail < 0)) gain *= VoxDials.TailGain;

            b.Pitch = Clamp(VoxDials.BasePitch * m.Pitch * SemiToRatio(semi), VoxDials.PitchMin, VoxDials.PitchMax);
            b.Gain = Clamp(gain, 0.02, 1);
        }
        return blips;
    }

    public static IReadOnlyList<VoxBlip> MakeScore(string? text, string? mood)
    {
        var m = mood != null && Moods.TryGetValue(mood, out var found) ? found : Moods["idle"];
        var seed = text ?? string.Empty;

        var words = Tokenize(seed);
        if (words.Count == 0) return Array.Empty<VoxBlip>();
        bool bang = words.Any(w => w.Bang);

        var score = Layout(words, m, bang, seed);
        // ponytail: WPF's fixed 240 guard can leave very long localized lines over both ceilings.
        // Each drop removes a syllable or a word, so this finite budget covers every possible drop.
        int budget = words.Sum(w => w.Syl) + words.Count;
        for (int guard = 0; guard < budget; guard++)
        {
            double dur = score.Count > 0 ? score[score.Count - 1].T : 0;
            if (score.Count <= VoxDials.MaxBlips && dur <= VoxDials.BurstMaxMs) break;
            if (!DropOne(words)) { score = score.Take(VoxDials.MaxBlips).ToList(); break; }
            score = Layout(words, m, bang, seed);
        }

        var outp = new VoxBlip[score.Count];
        for (int i = 0; i < score.Count; i++)
            outp[i] = new VoxBlip((int)Math.Round(score[i].T), score[i].Pitch, score[i].Gain);
        return outp;
    }


    private const int SampleRate = 44100;

    private const double VoiceBus = 0.85;

    private const double BlipF0 = 760.0, BlipF1 = 680.0, BlipMs = 56.0, BlipGain = 0.35, BlipAttack = 0.3;

    private static void MixTone(float[] buf, int atSample, double f0, double f1, double ms,
                                double recGain, double attackFrac, double level, double pitch)
    {
        double dur = Math.Max(0.02, ms / 1000.0);
        double atk = Math.Max(0.004, dur * attackFrac);
        double peak = Math.Max(0.0002, Math.Sqrt(Clamp(level, 0, 1)) * recGain * VoiceBus);

        double a = Math.Max(20.0, Math.Min(20000.0, f0 * pitch));
        double b = Math.Max(20.0, Math.Min(20000.0, f1 * pitch));

        int n = (int)(dur * SampleRate);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            int at = atSample + i;
            if (at < 0) continue;
            if (at >= buf.Length) break;

            double t = i / (double)SampleRate;
            double f = a * Math.Pow(b / a, t / dur);
            phase += f / SampleRate;

            double env = t < atk
                ? peak * (t / atk)
                : peak * Math.Pow(0.0001 / peak, (t - atk) / Math.Max(1e-6, dur - atk));

            double frac = phase - Math.Floor(phase);
            double tri = 4.0 * Math.Abs(frac - 0.5) - 1.0;

            buf[at] += (float)(tri * env);
        }
    }

    public static float[] RenderBurst(IReadOnlyList<VoxBlip> score)
    {
        if (score == null || score.Count == 0) return Array.Empty<float>();

        double endMs = score[score.Count - 1].AtMs + BlipMs + 20.0;
        var buf = new float[(int)(endMs / 1000.0 * SampleRate) + 8];
        foreach (var b in score)
        {
            MixTone(buf, (int)(b.AtMs / 1000.0 * SampleRate),
                    BlipF0, BlipF1, BlipMs, BlipGain, BlipAttack, b.Gain, b.Pitch);
        }
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)Clamp(buf[i], -1.0, 1.0);
        return buf;
    }

    public static byte[] WriteWav(float[] samples)
    {
        samples ??= Array.Empty<float>();
        int dataBytes = samples.Length * 2;
        var bytes = new byte[44 + dataBytes];

        void Ascii(int at, string s) { for (int i = 0; i < s.Length; i++) bytes[at + i] = (byte)s[i]; }
        void U32(int at, uint v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); bytes[at + 2] = (byte)(v >> 16); bytes[at + 3] = (byte)(v >> 24); }
        void U16(int at, ushort v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); }

        Ascii(0, "RIFF");
        U32(4, (uint)(36 + dataBytes));
        Ascii(8, "WAVE");
        Ascii(12, "fmt ");
        U32(16, 16);                       // PCM chunk size
        U16(20, 1);                        // format = PCM
        U16(22, 1);                        // channels = mono
        U32(24, SampleRate);
        U32(28, SampleRate * 2);           // byte rate (1 channel x 2 bytes)
        U16(32, 2);                        // block align
        U16(34, 16);                       // bits per sample
        Ascii(36, "data");
        U32(40, (uint)dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            double v = Clamp(samples[i], -1.0, 1.0);
            short sv = (short)Math.Round(v * short.MaxValue);
            bytes[44 + i * 2] = (byte)sv;
            bytes[44 + i * 2 + 1] = (byte)((ushort)sv >> 8);
        }
        return bytes;
    }

}
