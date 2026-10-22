using System;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.AudioPlayer;

// The native audio boundary; tests use a silent device, never the desktop's sound card.
internal interface IPlaylistAudioPlayer : IDisposable
{
    string? CurrentPath { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    long CurrentTimeMs { get; }
    long DurationMs { get; }
    int Volume { get; set; }
    bool Play(string path);
    void Resume();
    void Pause();
    void Stop();
    void Seek(double seconds);
    event Action<long>? PlaybackTimeMsChanged;
    event Action? Ended;
    event Action? StoppedUnexpectedly;
}

internal sealed class PlaylistAudioPlayer : IPlaylistAudioPlayer
{
    private MediaPlayer? _player;
    private Media? _media;
    private int _generation, _volume = 80;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public PlaylistAudioPlayer() => _timer.Tick += (_, _) => PlaybackTimeMsChanged?.Invoke(CurrentTimeMs);
    public string? CurrentPath { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public long CurrentTimeMs => Math.Max(0, _player?.Time ?? 0);
    public long DurationMs => Math.Max(0, _player?.Length ?? 0);
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            var p = _player;
            if (p != null) lock (p) if (ReferenceEquals(p, _player)) p.Volume = NativeVolume;
        }
    }
    private int NativeVolume => (int)Math.Round(Math.Cbrt(_volume / 100.0) * 100);
    public event Action<long>? PlaybackTimeMsChanged;
    public event Action? Ended;
    public event Action? StoppedUnexpectedly;
    public bool Play(string path)
    {
        Stop();
        if (!File.Exists(path) || LibVlcAudio.Shared is not { } vlc) return false;
        try
        {
            _media = new Media(vlc, path, FromType.FromPath);
            _media.AddOption(LibVlcAudio.NoVideo);
            var p = _player = new MediaPlayer(_media);
            int generation = _generation;
            p.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (p) if (ReferenceEquals(p, _player))
                { LibVlcAudio.ApplyPreferredDevice(p); p.Volume = NativeVolume; }
            });
            // A native callback must return before Stop/Dispose touches LibVLC again.
            p.EndReached += (_, _) => Dispatcher.UIThread.Post(() =>
            { if (generation == _generation) { IsPlaying = false; _timer.Stop(); Ended?.Invoke(); } });
            p.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() =>
            { if (generation == _generation) StoppedUnexpectedly?.Invoke(); });
            if (!p.Play()) { Stop(); return false; }
            CurrentPath = path;
            IsPlaying = true;
            _timer.Start();
            return true;
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Playlist file could not open"); Stop(); return false; }
    }
    public void Resume() { _player?.SetPause(false); IsPlaying = _player != null; IsPaused = false; _timer.Start(); }
    public void Pause()
    {
        if (_player?.State != VLCState.Playing) { Stop(); return; }
        _player.SetPause(true); IsPlaying = false; IsPaused = true; _timer.Stop();
    }
    public void Seek(double seconds) { if (_player is { } p && double.IsFinite(seconds)) p.Time = (long)(Math.Max(0, seconds) * 1000); }
    public void Stop()
    {
        _generation++;
        _timer.Stop();
        var p = _player; _player = null;
        var media = _media; _media = null;
        CurrentPath = null; IsPlaying = IsPaused = false;
        if (p != null) lock (p) { p.Stop(); p.Dispose(); }
        media?.Dispose();
    }
    public void Dispose() => Stop();
}
