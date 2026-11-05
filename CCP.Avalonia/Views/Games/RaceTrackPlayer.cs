// Racing Thoughts track player + decoder, ported from WPF Services/Race/TrackPlayer.cs (NAudio
// AudioFileReader + WaveOutEvent) and Services/Race/NAudioTrackDecode.cs (seeded at WPF App.xaml.cs:313).
// NAudio is Windows only; both halves here go through the port's shared LibVLC (Platform/LibVlcAudio.Shared),
// as trunk CCP.Avalonia/Views/Games/RaceWindow.TrackPlayer.cs does. Same face as WPF: master volume,
// Play / Pause / Resume / Stop, PositionSec for the 250 ms clock, Ended when the file runs out (never for
// a Stop the run asked for, WPF TrackPlayer.cs:152-157).
using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed class RaceTrackPlayer : IRaceTrackPlayer
    {
        /// <summary>Raised once when the file plays out (libvlc's thread; the host marshals).</summary>
        public event Action? Ended;

        private readonly object _gate = new();
        private Media? _media;
        private MediaPlayer? _player;
        /// <summary>True while a Stop, a restart or a teardown is why playback ends (WPF _stopping).</summary>
        private volatile bool _stopping;
        /// <summary>Latch: an ending is reported once (WPF _endedFired).</summary>
        private volatile bool _endedFired;
        private volatile bool _disposed;
        /// <summary>Play was asked for and not stopped since.</summary>
        private volatile bool _started;
        /// <summary>A Pause that arrived before libvlc reached Playing.</summary>
        private volatile bool _pausePending;

        public double PositionSec
        {
            get { try { return Math.Max(0, (_player?.Time ?? 0) / 1000.0); } catch { return 0; } }
        }

        public double DurationSec
        {
            get
            {
                try
                {
                    long ms = _player?.Length ?? 0;
                    if (ms <= 0) ms = _media?.Duration ?? 0;
                    return Math.Max(0, ms / 1000.0);
                }
                catch { return 0; }
            }
        }

        /// <summary>libvlc's state, for tests.</summary>
        internal VLCState State { get { try { return _player?.State ?? VLCState.NothingSpecial; } catch { return VLCState.Error; } } }

        public bool IsPlaying
        {
            get { try { return _player?.IsPlaying == true; } catch { return false; } }
        }

        /// <summary>WPF TrackPlayer.Load :63. Throws on a missing file or a machine without libvlc.</summary>
        public void Load(string path)
        {
            if (_disposed) return;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Track file not found", path ?? "");
            var vlc = LibVlcAudio.Shared ?? throw new InvalidOperationException(GameWindow.RaceNoAudioMessage);
            Unload();
            var media = new Media(vlc, path, FromType.FromPath);
            media.AddOption(LibVlcAudio.NoVideo);
            // The length before the first play, without blocking the UI thread: the parse finishes in the
            // background and the 250 ms track-clock carries DurationSec to the page once it is known.
            try { _ = media.Parse(MediaParseOptions.ParseLocal, 2000); }
            catch (Exception ex) { Log.Debug("RaceTrackPlayer: parse: {E}", ex.Message); }
            var player = new MediaPlayer(media);
            player.EndReached += OnEndReached;
            player.EncounteredError += OnError;
            player.Playing += OnPlaying;
            lock (_gate) { _media = media; _player = player; }
            _stopping = false;
            _endedFired = false;
            _started = false;
            _pausePending = false;
        }

        /// <summary>WPF Play :81: from the start, restarting a running track.</summary>
        public void Play()
        {
            var p = _player;
            if (_disposed || p == null) return;
            try
            {
                _stopping = true;                 // the restart's own stop must not read as an ending
                if (p.State is not (VLCState.Stopped or VLCState.NothingSpecial)) p.Stop();
                _stopping = false;
                _endedFired = false;
                _pausePending = false;
                _started = true;
                if (!p.Play()) Log.Warning("RaceTrackPlayer.Play: the player refused to start");
            }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Play: {E}", ex.Message); }
        }

        public void Pause()
        {
            var p = _player;
            if (_disposed || p == null) return;
            // A Brake right after Play lands while libvlc is still opening/buffering, where SetPause is
            // ignored: remember it and pause as soon as the track is Playing (WPF WaveOutEvent.Pause holds).
            try { lock (_gate) { if (p.IsPlaying) p.SetPause(true); else if (_started && p.State != VLCState.Paused) _pausePending = true; } }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Pause: {E}", ex.Message); }
        }

        public void Resume()
        {
            var p = _player;
            if (_disposed || p == null) return;
            try
            {
                // SetPause is async in libvlc: a Pause just sent can still read as Playing, so un-pause then too.
                lock (_gate)
                {
                    _pausePending = false;
                    if (p.State is VLCState.Paused or VLCState.Playing) p.SetPause(false);
                }
                ApplyVolume();
            }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Resume: {E}", ex.Message); }
        }

        /// <summary>WPF Stop :121: end playback for good; never raises Ended.</summary>
        public void Stop()
        {
            var p = _player;
            if (p == null) return;
            try
            {
                _stopping = true;
                _started = false;
                _pausePending = false;
                if (p.State is not (VLCState.Stopped or VLCState.NothingSpecial)) p.Stop();
            }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Stop: {E}", ex.Message); }
        }

        public void RefreshVolume() => ApplyVolume();

        /// <summary>WPF ApplyVolume :136: the app's master percentage. LibVLC's volume is cubic where
        /// WPF's is linear sample gain, hence the cube root (as LibVlcAudio).</summary>
        internal static int VlcVolume(int masterPercent) =>
            (int)Math.Round(Math.Cbrt(Math.Clamp(masterPercent / 100.0, 0, 1)) * 100);

        private void ApplyVolume()
        {
            var p = _player;
            if (p == null) return;
            int vol;
            try { vol = VlcVolume(CoreSettings.Current?.MasterVolume ?? 100); }
            catch { vol = 100; }
            // Off libvlc's own thread, and only once the output exists (a volume set earlier is lost).
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (_gate) if (!_disposed && ReferenceEquals(p, _player) && p.IsPlaying) p.Volume = vol; }
                catch (Exception ex) { Log.Debug("RaceTrackPlayer.ApplyVolume: {E}", ex.Message); }
            });
        }

        private void OnPlaying(object? sender, EventArgs e)
        {
            var p = _player;
            if (p == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (_gate) if (_pausePending && !_disposed && ReferenceEquals(p, _player)) { _pausePending = false; p.SetPause(true); } }
                catch (Exception ex) { Log.Debug("RaceTrackPlayer pending pause: {E}", ex.Message); }
                try { lock (_gate) if (!_disposed && ReferenceEquals(p, _player)) LibVlcAudio.ApplyPreferredDevice(p); }
                catch (Exception ex) { Log.Debug("RaceTrackPlayer device: {E}", ex.Message); }
            });
            ApplyVolume();
        }

        private void OnError(object? sender, EventArgs e) => Log.Warning("RaceTrackPlayer: playback stopped with an error");

        /// <summary>WPF OnPlaybackStopped :152: a stop we asked for is not an ending; anything else is the
        /// file running out and raises Ended once.</summary>
        internal void OnEndReached(object? sender, EventArgs e)
        {
            if (_stopping || _endedFired || _disposed) return;
            _endedFired = true;
            try { Ended?.Invoke(); }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Ended handler: {E}", ex.Message); }
        }

        /// <summary>WPF Unload :179: close the player and the media; reusable through Load.</summary>
        private void Unload()
        {
            _stopping = true;
            MediaPlayer? player; Media? media;
            lock (_gate) { player = _player; media = _media; _player = null; _media = null; }
            if (player == null && media == null) return;
            if (player != null)
            {
                player.EndReached -= OnEndReached;
                player.EncounteredError -= OnError;
                player.Playing -= OnPlaying;
            }
            // Never stop or dispose a player on a libvlc event thread, and never make the UI wait on it.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { player?.Stop(); } catch { }
                try { player?.Dispose(); } catch (Exception ex) { Log.Debug("RaceTrackPlayer.Unload player: {E}", ex.Message); }
                try { media?.Dispose(); } catch (Exception ex) { Log.Debug("RaceTrackPlayer.Unload media: {E}", ex.Message); }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unload();
            Ended = null;
        }

        // ============================ the decoder ============================

        /// <summary>Core TrackDecoder.DecodeProvider (WPF NAudioTrackDecode.Decode): any file libvlc reads,
        /// to mono 16 kHz float PCM, through a temporary 16-bit WAV. Progress is the decode position 0..1;
        /// cancel throws OperationCanceledException. Throws when the file will not decode.</summary>
        internal static float[] Decode(string path, IProgress<double>? progress, CancellationToken ct)
        {
            // Before the transcode: a short track can finish inside the first 100 ms wait, so the loop's check never runs.
            ct.ThrowIfCancellationRequested();
            var wav = Path.Combine(Path.GetTempPath(), "ccp-race-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                TranscodeToWav(path, wav, progress, ct);
                // A synchronous IProgress could cancel from Report(1), after the native wait. Real callers pass
                // Progress<double> (posted, so asynchronous); this guards the seam, and the test hits it directly.
                ct.ThrowIfCancellationRequested();
                return ReadMono16(File.ReadAllBytes(wav));
            }
            finally { try { File.Delete(wav); } catch { } }
        }

        /// <summary>The sout chain that writes 16 kHz mono signed 16-bit WAV to <paramref name="dest"/>.
        /// Forward slashes and an escaped quote, so a Windows path survives libvlc's option parser.</summary>
        internal static string SoutFor(string dest) =>
            ":sout=#transcode{vcodec=none,acodec=s16l,channels=1,samplerate=16000}:std{access=file,mux=wav,dst='"
            + dest.Replace('\\', '/').Replace("'", "\\'") + "'}";

        private static void TranscodeToWav(string source, string dest, IProgress<double>? progress, CancellationToken ct)
        {
            // A short file transcodes inside the first 100 ms wait, so an already-cancelled pick must stop here.
            ct.ThrowIfCancellationRequested();
            var vlc = LibVlcAudio.Shared ?? throw new InvalidOperationException(GameWindow.RaceNoAudioMessage);
            using var done = new ManualResetEventSlim(false);
            bool failed = false;
            using var media = new Media(vlc, source, FromType.FromPath);
            media.AddOption(SoutFor(dest));
            media.AddOption(":no-sout-video");
            media.AddOption(":sout-keep");
            using var player = new MediaPlayer(media);
            player.EndReached += (_, _) => done.Set();
            player.EncounteredError += (_, _) => { failed = true; done.Set(); };
            player.PositionChanged += (_, e) => { try { progress?.Report(Math.Clamp(e.Position, 0f, 1f)); } catch { } };
            if (!player.Play()) throw new InvalidDataException("the track would not open");
            try
            {
                // A sout chain has no clock, so it runs faster than real time; ten minutes means a wedged decoder.
                var limit = DateTime.UtcNow.AddMinutes(10);
                while (!done.Wait(100, ct))
                {
                    if (DateTime.UtcNow > limit) throw new TimeoutException("the track took too long to decode");
                }
            }
            finally
            {
                // The muxer patches the WAV header and closes the file on stop.
                try { player.Stop(); } catch (Exception ex) { Log.Debug("RaceTrackPlayer.Transcode stop: {E}", ex.Message); }
            }
            // EndReached can win the wait while cancellation arrives; Stop still runs before throwing.
            ct.ThrowIfCancellationRequested();
            if (failed) throw new InvalidDataException("the track would not decode");
            progress?.Report(1);
        }

        /// <summary>The "data" chunk of a mono 16-bit PCM WAV as floats in [-1, 1). A header the muxer never
        /// patched (size 0 or past the end) reads to the end of the file.</summary>
        internal static float[] ReadMono16(byte[] wav)
        {
            if (wav.Length < 12 || BitConverter.ToUInt32(wav, 0) != 0x46464952 || BitConverter.ToUInt32(wav, 8) != 0x45564157)
                throw new InvalidDataException("the track would not decode");
            int pos = 12;
            bool fmtOk = false;
            while (pos + 8 <= wav.Length)
            {
                uint id = BitConverter.ToUInt32(wav, pos);
                long size = BitConverter.ToUInt32(wav, pos + 4);
                pos += 8;
                if (id == 0x20746D66 && size >= 16)           // "fmt "
                    fmtOk = BitConverter.ToUInt16(wav, pos) == 1 && BitConverter.ToUInt16(wav, pos + 2) == 1
                            && BitConverter.ToUInt32(wav, pos + 4) == 16000 && BitConverter.ToUInt16(wav, pos + 14) == 16;
                else if (id == 0x61746164)                    // "data"
                {
                    if (!fmtOk) break;
                    long end = size == 0 || pos + size > wav.Length ? wav.Length : pos + size;
                    var pcm = new float[(end - pos) / 2];
                    for (int i = 0; i < pcm.Length; i++) pcm[i] = BitConverter.ToInt16(wav, pos + i * 2) / 32768f;
                    return pcm;
                }
                if (size > wav.Length - pos) break;
                pos += (int)(size + (size & 1));
            }
            throw new InvalidDataException("the track would not decode");
        }
    }
}
