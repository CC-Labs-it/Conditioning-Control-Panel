// WPF AvatarTubeWindow.CirceEmotes.cs:624-865, 885-948: speech-driven Rain clips.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Platform;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Rain;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private JObject? _rainTiming;
    private IDisposable? _rainSpeechTimer;
    private int _rainSpeechVersion;
    private bool _rainTalkActive, _rainSpeechOwned;
    private long _rainClipTick;
    internal Func<long> RainClock = static () => Environment.TickCount64;
    internal Func<TimeSpan, Action, IDisposable> RainSchedule = static (delay, fire) => DispatcherTimer.RunOnce(fire, delay);
    private IDisposable? _speechLeadIn;
    private readonly Dictionary<string, double> _rainAudioSeconds = new();

    // WPF AudioDurationSec (Avatar.cs:1259-1272): the clip length, read once per path before the line is planned.
    internal Func<string, double> RainAudioProbe = static path =>
    {
        if (LibVlcAudio.Shared is not { } vlc || !File.Exists(path)) return 0;
        try
        {
            using var media = new Media(vlc, path, FromType.FromPath);
            // Parsed off the UI context so libvlc's completion cannot wait on the blocked dispatcher.
            return Task.Run(() => media.Parse(MediaParseOptions.ParseLocal, 1000)).Result == MediaParsedStatus.Done
                ? Math.Max(0, media.Duration) / 1000.0 : 0;
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Rain voice length unavailable"); return 0; }
    };

    private double RainProbedSeconds(string? audioPath)
    {
        if (_rainMap == null || string.IsNullOrEmpty(audioPath)) return 0;
        if (!_rainAudioSeconds.TryGetValue(audioPath, out var sec)) _rainAudioSeconds[audioPath] = sec = RainAudioProbe(audioPath);
        return sec;
    }

    private void LoadRainTiming()
    {
        _rainTiming = null;
        try { using var r = new StreamReader(OpenRainFile("clip_timing.json")); _rainTiming = JObject.Parse(r.ReadToEnd()); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Rain clip timing unavailable"); }
    }

    private bool RainHasClip(string? clip)
    {
        if (string.IsNullOrEmpty(clip) || !RainPackReader.IsValidClip(clip) || _rainFolder == null) return false;
        if (_rainPack != null) return _rainPack.Clips.ContainsKey(clip);
        try { return Path.IsPathRooted(_rainFolder) ? File.Exists(Path.Combine(_rainFolder, clip + ".webp"))
            : AssetLoader.Exists(new Uri($"avares://CCP.Avalonia/Resources/{_rainFolder}/{clip}.webp")); }
        catch { return false; }
    }

    private List<string> RainPool(JToken? token) => (token as JArray)?.Select(c => (string?)c)
        .Where(RainHasClip).Select(c => c!).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new();

    private string? RainReaction(string? id, string? mood)
    {
        if (!string.IsNullOrEmpty(id) && _rainMap?["stemPrefix"] is JObject stems)
            foreach (var p in stems.Properties().OrderByDescending(p => p.Name.Length))
                if (id.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase) && RainHasClip((string?)p.Value)) return (string?)p.Value;
        var token = mood?.Split(',')[0].Trim();
        if (!string.IsNullOrEmpty(token) && _rainMap?["mood"] is JObject moods &&
            moods.TryGetValue(token, StringComparison.OrdinalIgnoreCase, out var value) && RainHasClip((string?)value)) return (string?)value;
        return null;
    }

    private string RainExpressive()
    {
        var pool = RainPool(_rainMap?["expressive"]?["pool"]);
        if (pool.Count > 1) pool.RemoveAll(c => string.Equals(c, _rainClip, StringComparison.OrdinalIgnoreCase));
        return pool.Count == 0 ? PickRainIdle() : pool[_random.Next(pool.Count)];
    }

    private (int start, int end) RainTalkTiming(string clip)
    {
        var t = _rainTiming?[clip];
        if (t == null) return (600, 3100);
        int duration = (int?)t["durationMs"] ?? 0;
        if (duration <= 0) duration = 2500;
        int start = Math.Clamp((int?)t["speakStartMs"] ?? 0, 0, duration - 1);
        int end = (int?)t["speakEndMs"] ?? 0;
        return (start, end > start ? Math.Min(end, duration) : duration);
    }

    private static readonly Regex RainNonverbal = new(
        @"^(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+)(?:[\s,.~!\-]+(?:\*[^*]+\*|gigg(?:le|les)|mm+|mh+m?|hm+|ah+|oo+h?|ha+|moans?|sighs?|teehee+|gasps?|purrs?|ohh+|uh+))*[\s,.~!\-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static double RainEstimatedSeconds(string? text) => string.IsNullOrWhiteSpace(text) ? 2.5 :
        Math.Clamp(0.45 * text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length + 0.8, 2.0, 7.0);

    // Each timer is relative to the voice start. Completion cannot advance during scheduled talk.
    private int StartRainSpeech(string? id, string? mood, string? text, string? audioPath, double seconds)
    {
        CancelRainClick(); CancelRainSpeech();
        if (_rainMap == null || _avatarSetsClosed || IsMuted) return 0;
        _rainSpeechOwned = true;
        int version = _rainSpeechVersion;
        int minHold = Math.Max(0, (int?)_rainMap["minClipMs"] ?? 2500);
        int hold = _rainClip == null ? 0 : (int)Math.Clamp(minHold - (RainClock() - _rainClipTick), 0, minHold);
        bool nonverbal = !string.IsNullOrWhiteSpace(text) ? RainNonverbal.IsMatch(text.Trim()) :
            !string.IsNullOrEmpty(audioPath) && seconds > 0 && seconds <= ((double?)_rainMap["expressive"]?["nonverbalMaxSec"] ?? 1.4);
        string reaction = nonverbal ? RainExpressive() : RainReaction(id, mood) ?? RainExpressive();
        int delay = Math.Max(hold, Math.Max(0, (int?)_rainMap["talkStartDelayMs"] ?? 1000));
        int leadOut = Math.Max(0, (int?)_rainMap["talkLeadOutMs"] ?? 500);
        int voiceMs = (int)Math.Round(Math.Clamp(seconds, 0, 86400) * 1000);
        var pool = RainPool(_rainMap["talking"]?["pool"]);
        if (nonverbal || voiceMs - delay - leadOut < 500 || pool.Count == 0)
        {
            RainAfter(hold, version, () => { _rainTalkActive = false; _ = PlayRainClip(reaction); });
            return nonverbal ? Math.Clamp(hold + 250, 0, 3000) : 0;
        }
        var fast = pool.Where(c => RainTalkTiming(c).start <= 700).ToList();
        string first = fast.Count > 0 ? fast[_random.Next(fast.Count)] : pool.OrderBy(c => RainTalkTiming(c).start).First();
        var talk = new List<string> { first };
        var remaining = pool.Where(c => c != first).OrderBy(_ => _random.Next()).ToList();
        int covered = RainTalkTiming(first).end - RainTalkTiming(first).start;
        int max = Math.Max(1, (int?)_rainMap["talking"]?["maxTalkClips"] ?? 3);
        foreach (var clip in remaining)
        {
            if (covered >= voiceMs || talk.Count >= max) break;
            talk.Add(clip); var t = RainTalkTiming(clip); covered += t.end - t.start;
        }
        RainAfter(delay, version, () =>
        {
            _rainTalkActive = true;
            _ = PlayRainClip(first);
            long deadline = voiceMs - leadOut - delay, lastStart = 0, coveredEnd = RainTalkTiming(first).end;
            var events = new Queue<(long at, string clip, bool reaction)>();
            for (int i = 1; i < talk.Count; i++)
            {
                var t = RainTalkTiming(talk[i]);
                long at = Math.Max(lastStart + minHold, coveredEnd - t.start);
                if (at >= deadline) break;
                events.Enqueue((at, talk[i], false)); lastStart = at; coveredEnd = at + t.end;
            }
            if (coveredEnd + 250 < deadline)
            {
                long at = Math.Max(lastStart + minHold, coveredEnd - RainTalkTiming(talk[^1]).start);
                if (at < deadline) events.Enqueue((at, talk[^1], false));
            }
            events.Enqueue((deadline, reaction, true));
            long start = RainClock();
            void Next()
            {
                if (events.Count == 0) return;
                var e = events.Dequeue();
                RainAfter((int)Math.Max(1, e.at - (RainClock() - start)), version, () =>
                {
                    if (e.reaction) _rainTalkActive = false;
                    _ = PlayRainClip(e.clip);
                    if (!e.reaction) Next();
                });
            }
            Next();
        });
        return 0;
    }

    private void RainAfter(int milliseconds, int version, Action action)
    {
        _rainSpeechTimer?.Dispose();
        if (milliseconds <= 0) { if (version == _rainSpeechVersion) action(); return; }
        _rainSpeechTimer = RainSchedule(TimeSpan.FromMilliseconds(milliseconds), () =>
        {
            if (version == _rainSpeechVersion && !_avatarSetsClosed && !IsMuted) { _rainSpeechTimer = null; action(); }
        });
    }

    private void CancelRainSpeech()
    {
        ++_rainSpeechVersion; _rainSpeechTimer?.Dispose(); _rainSpeechTimer = null;
        _rainTalkActive = false; _rainSpeechOwned = false;
    }

    private void MuteRainSpeech()
    {
        StopRainSpeech();
        try { _stopSpoken(); } catch { }
        _stopSpoken = static () => { };
    }

    private void StopRainSpeech()
    {
        bool owned = _rainSpeechOwned;
        CancelRainClick(); CancelRainSpeech(); _rainFailures = 0;
        if (!owned || _rainMap == null) return;
        ++_rainVersion; _rainFade?.Dispose(); _rainFade = null;
        foreach (var player in _rainPlayers) player?.Stop();
        _rainClip = null;
        if (!_avatarSetsClosed) _ = PlayRainClip(PickRainIdle());
    }
}
