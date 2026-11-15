using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Serilog;

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

internal sealed class NativeMandatoryPlayback : IMandatoryPlayback
{
    private readonly MediaPlayer player;
    private readonly AudioTrackFocus _audio;
    private bool _disposed;

    public NativeMandatoryPlayback(MediaPlayer player)
    {
        this.player = player;
        _audio = new(() => player.AudioTrack, player.SetAudioTrack, () => player.AudioTrackDescription.Select(d => d.Id));
        // LibVLC picks the track after Play: a silenced player deselects it as it is chosen, not at the next watch tick.
        player.ESSelected += (_, e) =>
        {
            if (e.Type == TrackType.Audio) Dispatcher.UIThread.Post(() =>
            {
                try { if (!_disposed) _audio.Selected(); }
                catch (Exception ex) { Log.Debug("VideoService: audio track hold failed - {Error}", ex.Message); }
            });
        };
    }

    public long Time { get => player.Time; set => player.Time = value; }
    public long Length => player.Length;
    public bool IsPlaying => player.IsPlaying;
    public bool IsSeekable => player.IsSeekable;
    public bool HasVideo => player.VideoTrackCount != 0;
    public void SetPause(bool paused) => player.SetPause(paused);
    public void SetAudioOff(bool off) => _audio.Set(off);
    public void Stop() => player.Stop();
    public void Dispose() { _disposed = true; player.Dispose(); }
}

/// <summary>WPF ApplyPrimaryAudioTrack (VideoService.FloatingInstances.cs:378-401): silence ONE player through its own
/// audio track (LibVLC volume can be process-wide). The wish outlives a track that does not exist yet.</summary>
internal sealed class AudioTrackFocus(Func<int> current, Func<int, bool> select, Func<IEnumerable<int>> ids)
{
    private int _track = -1;
    private bool _everOff, _off;

    public void Set(bool off)
    {
        if (!off && !_everOff) return;
        _everOff = true;
        _off = off;
        var now = current();
        if (off) { if (now != -1) { _track = now; select(-1); } return; }
        if (now != -1) return;
        var id = _track != -1 ? _track : ids().FirstOrDefault(i => i != -1, -1);
        if (id != -1) select(id);
    }

    /// <summary>LibVLC selected an audio track (startup, or later): a silenced player drops it again.</summary>
    public void Selected() { if (_off) Set(true); }
}
