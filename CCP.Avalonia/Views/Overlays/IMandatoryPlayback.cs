using System;
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
}

internal sealed class NativeMandatoryPlayback(MediaPlayer player) : IMandatoryPlayback
{
    public long Time { get => player.Time; set => player.Time = value; }
    public long Length => player.Length;
    public bool IsPlaying => player.IsPlaying;
    public bool IsSeekable => player.IsSeekable;
    public bool HasVideo => player.VideoTrackCount != 0;
    public void SetPause(bool paused) => player.SetPause(paused);
    public void Stop() => player.Stop();
    public void Dispose() => player.Dispose();
}
