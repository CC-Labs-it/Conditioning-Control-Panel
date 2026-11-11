using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;

namespace ConditioningControlPanel.Avalonia.Platform
{
    // WPF Services/Audio/MediaFoundationRangeReader.cs:17. Core's analyzer uses 44.1 kHz.
    internal static class AudioRangeReader
    {
        internal static float[] Read(string path, TimeSpan start, TimeSpan end, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (start < TimeSpan.Zero || end <= start) throw new ArgumentOutOfRangeException(nameof(end));
            var wav = Path.Combine(Path.GetTempPath(), "ccp-haptic-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var sout = RaceTrackPlayer.SoutFor(wav).Replace("samplerate=16000", "samplerate=44100");
                RaceTrackPlayer.TranscodeToWav(path, wav, null, ct, sout, start, end);
                ct.ThrowIfCancellationRequested();
                return RaceTrackPlayer.ReadMono16(File.ReadAllBytes(wav), 44100);
            }
            finally { try { File.Delete(wav); } catch { } }
        }
    }
}
