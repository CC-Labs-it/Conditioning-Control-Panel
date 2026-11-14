using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Port of WPF's LayeredAudioService (ConditioningControlPanel/Services/Audio/LayeredAudioService.cs)
    /// for suggestion #659 "Audio Layers". Same public surface and the same rules: Start rebuilds
    /// from settings (enabled tracks with an existing file, skipped otherwise), Restart keeps the
    /// running state, live per-track and master volume, cooperative duck. No fades - WPF has none.
    /// Deviation: one LibVLC player per track instead of one NAudio mixer; the OS mixes the streams.
    /// Gain per track = track% x layers master% x app master% x duck factor, as linear sample
    /// gain like WPF, mapped onto LibVLC's cubic volume.
    /// </summary>
    internal sealed class LayeredAudio : IDisposable
    {
        /// <summary>One looping track. The seam exists so the state machine is testable without libvlc.</summary>
        internal interface ILayerPlayer : IDisposable { int Volume { set; } void Publish(int volume) { } }

        private readonly Func<string, int, ILayerPlayer> _open;
        private readonly object _lock = new();
        private readonly Dictionary<AudioLayerTrack, ILayerPlayer> _players = new();
        private float _duckFactor = 1f;
        private bool _disposed;
        private readonly Dictionary<ILayerPlayer, int> _pendingVolume = new();
        private readonly Dictionary<ILayerPlayer, Task> _volumeWriters = new();

        internal static LayeredAudio? Instance { get; set; }

        public LayeredAudio(Func<string, int, ILayerPlayer> open) => _open = open;

        public bool IsPlaying { get { lock (_lock) return _players.Count > 0; } }

        public void Start(bool ignoreMasterToggle = false)
        {
            lock (_lock)
            {
                if (_disposed) return;
                StopInternal();
                var s = CoreSettings.Current;
                if (s == null || (!s.AudioLayersEnabled && !ignoreMasterToggle) || s.AudioLayers == null) return;
                foreach (var track in s.AudioLayers)
                {
                    if (track == null || !track.Enabled) continue;
                    if (string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path))
                    {
                        Log.Debug("LayeredAudio: skipping missing/unset track '{Path}'", track.Path);
                        continue;
                    }
                    try
                    {
                        // WPF LayeredAudioService.cs:89-121 sets both gains before playback.
                        var p = _open(track.Path, VolumeFor(track.Volume, s));
                        _players[track] = p;
                    }
                    catch (Exception ex) { Log.Warning("LayeredAudio: could not load '{Path}': {Error}", track.Path, ex.Message); }
                }
                Log.Information("LayeredAudio: started with {Count} track(s)", _players.Count);
            }
        }

        public void Stop() { lock (_lock) StopInternal(); }

        public void Restart()
        {
            lock (_lock)
            {
                if (_disposed) return;
                var wasPlaying = _players.Count > 0;
                StopInternal();
                if (wasPlaying || CoreSettings.Current?.AudioLayersEnabled == true) Start();
            }
        }

        public void SetTrackVolumeLive(AudioLayerTrack track, int volumePercent)
        {
            lock (_lock)
                if (_players.TryGetValue(track, out var p))
                    QueueVolumes(new[] { (track, p, VolumeFor(volumePercent, CoreSettings.Current)) });
        }

        public void SetMasterVolumeLive()
        {
            lock (_lock)
                QueueVolumes(_players.Select(pair => (pair.Key, pair.Value, VolumeFor(pair.Key.Volume, CoreSettings.Current))).ToArray());
        }

        public void ApplyDuck(float amount)
        {
            lock (_lock) { _duckFactor = Math.Clamp(1f - amount, 0f, 1f); SetMasterVolumeLive(); }
        }

        public void ReleaseDuck()
        {
            lock (_lock) { _duckFactor = 1f; SetMasterVolumeLive(); }
        }

        // One writer per player, off the caller, always sending that player's newest gain: a player
        // whose write is slow holds up neither the caller nor any other player's duck.
        private void QueueVolumes((AudioLayerTrack Track, ILayerPlayer Player, int Volume)[] updates)
        {
            foreach (var (_, player, volume) in updates)
            {
                player.Publish(volume);   // publish before queued native work
                _pendingVolume[player] = volume;
                if (!_volumeWriters.ContainsKey(player)) _volumeWriters[player] = Task.Run(() => WriteVolumes(player));
            }
        }

        private void WriteVolumes(ILayerPlayer player)
        {
            while (true)
            {
                int volume;
                lock (_lock)
                    if (!_pendingVolume.Remove(player, out volume) || !_players.ContainsValue(player))
                    { _volumeWriters.Remove(player); return; }
                try { player.Volume = volume; }
                catch (Exception ex) { Log.Debug(ex, "LayeredAudio: volume update"); }
            }
        }

        internal Task VolumeUpdates { get { lock (_lock) return Task.WhenAll(_volumeWriters.Values.ToArray()); } }

        /// <summary>LibVLC volume 0-100 for a track, from WPF's linear gain chain.</summary>
        private int VolumeFor(int trackPercent, AppSettings s)
        {
            var gain = Math.Clamp(trackPercent / 100f, 0f, 1f)
                     * Math.Clamp(s.AudioLayersMasterVolume / 100f, 0f, 1f)
                     * Math.Clamp(s.MasterVolume / 100f, 0f, 1f)
                     * _duckFactor;
            return (int)Math.Round(Math.Cbrt(gain) * 100); // same cubic mapping as LibVlcAudio.PlayOneShot
        }

        private void StopInternal()
        {
            foreach (var p in _players.Values) { try { p.Dispose(); } catch { } }
            _players.Clear();
            _pendingVolume.Clear();
        }

        public void Dispose()
        {
            lock (_lock) { if (_disposed) return; _disposed = true; StopInternal(); }
        }

        /// <summary>App exit: stop every player synchronously (bounded), as WPF disposes its WaveOut.</summary>
        internal void Shutdown()
        {
            Task[] closing;
            lock (_lock)
            {
                closing = _players.Values.OfType<VlcLayerPlayer>().Select(p => p.Closing).ToArray();
                Dispose();
            }
            Task.WaitAll(closing, 2000);
        }

        /// <summary>A LibVLC player that loops its file forever. WPF's LoopingSampleProvider rewinds
        /// with no gap; reopening one player at EOF (Stop/Play) left a gap and opened the new stream
        /// before our volume stuck. So it loops as <see cref="LibVlcAudio"/>'s LoopVoice does: the
        /// next copy starts <see cref="OverlapMs"/> before the current one ends, muted until its
        /// volume sticks.</summary>
        internal sealed class VlcLayerPlayer : ILayerPlayer
        {
            private const int OverlapMs = 120;   // as LoopVoice (WPF CROSSFADE_OVERLAP_SECONDS)
            private sealed class Copy(MediaPlayer player, Media media)
            {
                public readonly MediaPlayer Player = player;
                public readonly Media Media = media;
                public bool Playing, Unmuted; public volatile bool Disposed;
                public GainWorker Gain = null!;
            }

            private readonly LibVLC _vlc;
            private readonly string _path;
            private readonly object _gate = new();
            private readonly List<Copy> _live = new();
            private readonly TaskCompletionSource _closed = new();
            private Copy? _newest, _timerFor;
            private readonly TimeProvider _time;
            private ITimer? _next;
            private readonly GainFence _volume;
            private volatile bool _volumeConfigured;
            private volatile bool _disposed;

            public Task Closing => _closed.Task;
            /// <summary>Copies started so far, and how many of them started with nothing else sounding (a gap).</summary>
            internal int Copies, GapStarts;
            internal (bool Playing, bool Muted, int Volume)? CurrentPlayback
            {
                get { lock (_gate) { if (_newest is not { } c) return null; lock (c) return c.Disposed ? null : (c.Playing, c.Player.Mute, c.Player.Volume); } }
            }
            /// <summary>The next pass is armed on the clock (it starts before this one ends, not at EOF).</summary>
            internal bool Armed { get { lock (_gate) return _timerFor != null && _timerFor == _newest; } }

            /// <param name="initialVolume">Known before playback for layers; null leaves a drone muted
            /// until its first volume is supplied.</param>
            public VlcLayerPlayer(LibVLC vlc, string path, int? initialVolume = null, TimeProvider? time = null)
            {
                _time = time ?? TimeProvider.System;
                _vlc = vlc;
                _path = path;
                _volume = new GainFence(initialVolume.GetValueOrDefault());
                _volumeConfigured = initialVolume.HasValue;
                StartCopy(after: null);
            }

            /// <summary>Starts the next pass, unless one already followed <paramref name="after"/>.</summary>
            private void StartCopy(Copy? after)
            {
                Copy c;
                lock (_gate)
                {
                    if (_disposed || _newest != after) return;
                    var media = new Media(_vlc, _path, FromType.FromPath);
                    media.AddOption(LibVlcAudio.NoVideo);
                    c = new Copy(new MediaPlayer(media), media);
                    // As in LibVlcAudio: volume set before Playing, or on libvlc's own thread, is lost.
                    c.Player.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ => OnPlaying(c));
                    c.Player.TimeChanged += (_, e) => { var t = e.Time; ThreadPool.QueueUserWorkItem(_ => Schedule(c, t)); };
                    c.Player.EndReached += (_, _) => ThreadPool.QueueUserWorkItem(_ => Retire(c, failed: false));
                    c.Player.EncounteredError += (_, _) => ThreadPool.QueueUserWorkItem(_ => Retire(c, failed: true));
                    c.Player.Mute = true;   // a pass stays muted until zero gain sticks
                    c.Gain = new GainWorker(() => _disposed || c.Disposed ? null : _volume.Value, v =>
                    {
                        lock (c)
                        {
                            if (c.Disposed) return true;
                            try { var gain = c.Unmuted ? v : 0; c.Player.Volume = gain; return c.Player.Volume == gain; }
                            catch (Exception ex) { Log.Debug(ex, "LayeredAudio: volume update"); return false; }
                        }
                    }, v => { lock (c) return _disposed || c.Disposed || c.Unmuted ? Task.FromResult(false) : _volume.QueueUnmute(v, () =>
                    {
                        lock (c) {
                            if (_disposed || c.Disposed) return false;
                            try { c.Player.Mute = false; c.Unmuted = true; return true; } // apply the latest gain
                            catch (Exception ex) { Log.Debug(ex, "LayeredAudio: unmute"); return true; } // retry
                        }
                    }); });
                    if (Copies > 0 && _live.Count == 0) GapStarts++;
                    Copies++;
                    _live.Add(c);
                    _newest = c;
                    if (c.Player.Play()) return;
                }
                Retire(c, failed: true);
            }

            private void OnPlaying(Copy c)
            {
                lock (_gate)
                {
                    if (_disposed || !_live.Contains(c)) return;
                    c.Playing = true;
                }
                ConditioningControlPanel.Services.Audio.RecentSounds.Shared.Note("layered-audio", _path, CoreEngine.IsRunning, false, DateTime.Now);   // WPF LayeredAudioService.cs:126
                Schedule(c, 0);
                lock (c) { if (c.Disposed) return; LibVlcAudio.ApplyPreferredDevice(c.Player); }
                if (!_disposed && _volumeConfigured) c.Gain.Kick();
            }

            /// <summary>Start the next pass <see cref="OverlapMs"/> before this one ends, measured from its
            /// play position (re-aimed on every TimeChanged), so a slow or fast output clock cannot open a gap.</summary>
            private void Schedule(Copy c, long timeMs)
            {
                lock (_gate)
                {
                    // A failed pass stays _newest after Retire freed it; a TimeChanged queued before its error
                    // must not read Length from the freed player (restack fixw4d).
                    if (_disposed || _newest != c || !c.Playing || c.Disposed) return;
                    var length = c.Player.Length;
                    if (length <= 0) return;   // unknown length: Retire restarts at the end
                    var due = Math.Max(100, length - OverlapMs) - timeMs;   // LoopVoice's 100 ms floor
                    if (due > 0)
                    {
                        _timerFor = c;
                        (_next ??= _time.CreateTimer(_ => { Copy? f; lock (_gate) f = _timerFor; if (f != null) StartCopy(f); }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan))
                            .Change(TimeSpan.FromMilliseconds(due), Timeout.InfiniteTimeSpan);
                        return;
                    }
                }
                StartCopy(c);
            }

            /// <summary>Runs on the thread pool, never on libvlc's thread. A copy that ends with nothing
            /// else sounding (unknown length) restarts at once; a failed one ends the loop.</summary>
            private void Retire(Copy c, bool failed)
            {
                bool restart;
                lock (_gate)
                {
                    if (!_live.Remove(c)) return;
                    DisposeCopy(c);
                    restart = !_disposed && !failed && _live.Count == 0;
                }
                if (restart) StartCopy(c);
            }

            private static void DisposeCopy(Copy c)
            {
                lock (c)
                {
                    c.Disposed = true;
                    try { c.Player.Stop(); c.Player.Dispose(); c.Media.Dispose(); }
                    catch (Exception ex) { Log.Debug(ex, "LayeredAudio: dispose"); }
                }
            }

            public int Volume
            {
                set
                {
                    if (_disposed) return;   // before _gate: teardown holds it through native Stop
                    Copy[] playing;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        _volume.SetUnlessPublished(value);   // a queued write may be older than the published gain
                        _volumeConfigured = true;
                        playing = _live.Where(c => c.Playing).ToArray();
                    }
                    foreach (var c in playing) c.Gain.Kick();
                }
            }

            /// <summary>The newest layer gain before its queued write: a pass that confirms the
            /// older gain then sees a different target and stays muted.</summary>
            public void Publish(int volume) { _volume.Publish(volume); _volumeConfigured = true; }

            internal sealed class GainFence(int initial)
            {
                private volatile int _value = initial; private bool _published; private readonly object _sync = new();
                public int Value => _value;
                public void Publish(int value) { lock (_sync) { _value = value; _published = true; } }
                public void SetUnlessPublished(int value) { lock (_sync) if (!_published) _value = value; }
                public void TryUnmute(int value, Action unmute) { lock (_sync) if (_value == value) unmute(); }
                public Task<bool> QueueUnmute(int value, Func<bool> unmute) { Task<bool>? work = null; TryUnmute(value, () => work = Task.Run(unmute)); return work ?? Task.FromResult(false); }
            }

            /// <summary>Per pass: pulse drops a volume set too soon after the stream opens, so retry the newest
            /// target off the caller until it sticks or the pass retires. Confirm zero before unmuting.</summary>
            internal sealed class GainWorker(Func<int?> target, Func<int, bool> trySet, Func<int, Task<bool>> unmute)
            {
                private int _busy;

                public void Kick() { if (Interlocked.Exchange(ref _busy, 1) == 0) _ = Task.Run(Run); }

                private async Task Run()
                {
                    while (true)
                    {
                        int? v;
                        while ((v = target()) is { } want && !trySet(want)) await Task.Delay(25).ConfigureAwait(false);
                        if (v is { } confirmed && target() == confirmed && await unmute(confirmed).ConfigureAwait(false)) { await Task.Delay(25).ConfigureAwait(false); continue; }
                        Volatile.Write(ref _busy, 0);
                        // A target set while we held _busy skipped its own Kick: take it on unless another did.
                        if (v == null || target() == v || Interlocked.Exchange(ref _busy, 1) != 0) return;
                    }
                }
            }

            public void Dispose()
            {
                // Retire immediately; native Stop itself can block and runs off the caller.
                _disposed = true;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        lock (_gate)
                        {
                            _disposed = true;
                            _next?.Dispose();
                            foreach (var c in _live) DisposeCopy(c);
                            _live.Clear();
                        }
                    }
                    finally { _closed.TrySetResult(); }
                });
            }
        }
    }
}
