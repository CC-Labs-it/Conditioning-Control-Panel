using System;
using System.Linq;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// Decoder boundary for deterministic seek, pause, cap and teardown tests without native allocation.
internal interface IMandatoryPlayback : IDisposable
{
    long Time { get; set; }
    long Length { get; }
    bool IsPlaying { get; }
    bool IsSeekable { get; }
    bool HasVideo { get; }
    void SetPause(bool paused);
    void Stop();
    /// <summary>Floating audio focus: silence this player alone (WPF VideoService.FloatingInstances.cs:303-351).</summary>
    void SetAudioOff(bool off) { }
}

internal sealed class NativeMandatoryPlayback(MediaPlayer player) : IMandatoryPlayback
{
    public long Time { get => player.Time; set => player.Time = value; }
    public long Length => player.Length;
    public bool IsPlaying => player.IsPlaying;
    public bool IsSeekable => player.IsSeekable;
    public bool HasVideo => player.VideoTrackCount != 0;
    public void SetPause(bool paused) => player.SetPause(paused);
    private int _track = -1;
    private bool _everOff;
    // WPF ApplyPrimaryAudioTrack: deselect THIS player's audio track (LibVLC volume can be process-wide).
    public void SetAudioOff(bool off)
    {
        if (!off && !_everOff) return;
        _everOff = true;
        var current = player.AudioTrack;
        if (off) { if (current != -1) { _track = current; player.SetAudioTrack(-1); } return; }
        if (current != -1) return;
        var id = _track != -1 ? _track : player.AudioTrackDescription.Select(d => d.Id).FirstOrDefault(i => i != -1, -1);
        if (id != -1) player.SetAudioTrack(id);
    }
    public void Stop() => player.Stop();
    public void Dispose() => player.Dispose();
}
