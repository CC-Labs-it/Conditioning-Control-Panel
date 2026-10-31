using System;
using System.IO;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF Services/Chaos/ChaosNarrator.cs:92-119,135-205: conversation-card voice.
/// Cards own their text and hold one duck across manual advances. Reset releases the floor,
/// leaving already started one-shots to finish, as WPF does. General narrative cues remain separate.</summary>
internal static class ChaosCardNarrator
{
    private static readonly object Gate = new();
    private static DateTimeOffset _busyUntil;
    private static long _duckGen = -1;
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;
    internal static Func<string, double?> ClipMilliseconds { get; set; } = Probe;
    internal static Func<string[]> Roots { get; set; } = () =>
        new[] { Path.Combine(CorePaths.UserData, "assets"), AppContext.BaseDirectory };

    internal static bool IsPlaying { get { lock (Gate) return Clock.GetUtcNow() < _busyUntil; } }

    internal static int PlayCardLine(string? audioKey, string text)
    {
        try
        {
            var path = ResolveAudio(audioKey);
            double? clip = path == null ? null : ClipMilliseconds(path);
            int ms = (int)Math.Clamp(clip is > 0 ? clip.Value + 250 : 1200 + (text?.Length ?? 0) * 55, 2400, 7000);
            lock (Gate)
            {
                _busyUntil = Clock.GetUtcNow().AddMilliseconds(ms + 200);
                if (_duckGen < 0)
                {
                    CoreAudio.Duck(72);
                    _duckGen = CoreAudio.DuckGeneration;
                }
            }
            if (path != null) CoreAudio.PlayOneShot(path, CoreSettings.Current.MasterVolume / 100f, "chaos-narrator");
            return ms;
        }
        catch (Exception ex) { Log.Debug(ex, "Chaos card voice failed"); return 2400; }
    }

    internal static void EndCard()
    {
        lock (Gate)
        {
            _busyUntil = default;
            if (_duckGen < 0) return;
            CoreAudio.Unduck(_duckGen);
            _duckGen = -1;
        }
    }

    internal static string? ResolveAudio(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        // WPF uses the default user assets root, then the install root, each with assets/Chaos/narrator.
        foreach (var root in Roots())
            foreach (var ext in new[] { ".mp3", ".wav" })
            {
                var path = Path.Combine(root, "assets", "Chaos", "narrator", key + ext);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    private static double? Probe(string path)
    {
        try
        {
            if (LibVlcAudio.Shared is not { } vlc) return null;
            using var media = new Media(vlc, path, FromType.FromPath);
            // Local bounded metadata read, equivalent to WPF AudioFileReader.TotalTime.
            return media.Parse(MediaParseOptions.ParseLocal, 2000).GetAwaiter().GetResult() == MediaParsedStatus.Done
                && media.Duration > 0 ? media.Duration : null;
        }
        catch { return null; }
    }
}
