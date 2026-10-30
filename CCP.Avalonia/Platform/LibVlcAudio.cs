using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Distro-check F3: LibVLCSharp P/Invokes "libvlc"/"libvlccore", which .NET probes only as
    /// libvlc.so; Debian/Ubuntu/Fedora runtime packages ship just the soname (libvlc.so.5,
    /// libvlccore.so.9; the bare .so is in -dev). Resolve to the unversioned name, then the soname.
    /// </summary>
    internal static class LibVlcNative
    {
        private static readonly Dictionary<string, string[]> Candidates = new()
        {
            ["libvlc"] = new[] { "libvlc.so", "libvlc.so.5" },
            ["libvlccore"] = new[] { "libvlccore.so", "libvlccore.so.9" },
        };
        private static int _installed;

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) == 1) return;   // one resolver per assembly
            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(Core).Assembly, (name, _, _) =>
                Resolve(name, f => System.Runtime.InteropServices.NativeLibrary.TryLoad(f, out var h) ? h : IntPtr.Zero));
        }

        /// <summary>First candidate <paramref name="tryLoad"/> opens; Zero = let .NET probe as usual.</summary>
        internal static IntPtr Resolve(string name, Func<string, IntPtr> tryLoad)
        {
            if (!Candidates.TryGetValue(name, out var files)) return IntPtr.Zero;
            foreach (var f in files)
            {
                var h = tryLoad(f);
                if (h != IntPtr.Zero) return h;
            }
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// The Avalonia head's answer to <see cref="CoreAudio"/>: one shared LibVLC, one
    /// MediaPlayer per one-shot, disposed when the clip ends. Ducking lowers OTHER apps'
    /// PulseAudio/PipeWire sink-inputs through <c>pactl</c> on Linux and restores them on
    /// Unduck, with WPF's ref count and generation rules.
    /// </summary>
    internal sealed class LibVlcAudio
    {
        private readonly LibVLC _vlc;
        private readonly Func<string, string> _pactl = Pactl;
        private readonly TimeProvider _duckClock = TimeProvider.System;

        /// <summary>The seeded instance, so App can restore ducked apps on exit.</summary>
        internal static LibVlcAudio? Instance { get; set; }

        /// <summary>The process's one LibVLC, shared with video (MiniPlayerWindow), as WPF shares
        /// VideoService.SharedLibVLC. Null when libvlc did not load.</summary>
        internal static LibVLC? Shared { get; private set; }

        /// <summary>Throws when libvlc is not installed; the caller then leaves CoreAudio unseeded.</summary>
        public LibVlcAudio(params string[] options)
        {
            // Windows ships libvlc beside the exe; a single-file publish has no
            // assembly location for LibVLCSharp to probe from, so point it there as WPF's VideoService does. Linux: system libvlc.
            // publish\libvlc (CopyLibVLCAfterPublish), else the build output's libvlc\win-x64.
            var bundled = new[] { "", Environment.Is64BitProcess ? "win-x64" : "win-x86" }
                .Select(sub => System.IO.Path.Combine(AppContext.BaseDirectory, "libvlc", sub))
                .FirstOrDefault(d => System.IO.File.Exists(System.IO.Path.Combine(d, "libvlc.dll")));
            if (OperatingSystem.IsWindows() && bundled != null) Core.Initialize(bundled);
            else { if (OperatingSystem.IsLinux()) LibVlcNative.Install(); Core.Initialize(); }
            // No global --no-video: video shares this instance. Audio media opt out per item instead.
            _vlc = new LibVLC(WithOutput(options, OperatingSystem.IsWindows()).Append("--quiet").ToArray());
            Shared ??= _vlc;
            _vlc.SetUserAgent("Conditioning Control Panel", "CCP"); // the name pactl shows for our streams
        }

        /// <summary>This instance's LibVLC (the Windows session test plays through the real output).</summary>
        internal LibVLC Vlc => _vlc;

        /// <summary>
        /// Windows: LibVLC's default output (mmdevice) sets Player.Volume and Mute on the PROCESS's one
        /// WASAPI session, which every player shares and Windows remembers (measured with silent WAVs:
        /// players at 100 and 30 shared ONE session, it went to 0.027 and both read 30). Through DirectSound
        /// each player's Volume/Mute is its own buffer's: measured 100 / 30 / 0 / 5 / mute on two players with
        /// the session at 1.000 throughout, each player reading its own value. 0 is DirectSound's floor
        /// (silence) and LibVLC's cubic scale is kept, so the linear gain mapping (<see cref="VlcVolume"/>)
        /// still lands WPF's sample-gain levels. ",any" falls back to the default output if DirectSound
        /// cannot open. A caller's own --aout (tests: dummy) wins. Linux: pulse is per stream already.
        /// </summary>
        internal static string[] WithOutput(string[] options, bool windows) =>
            windows && !options.Any(o => o.StartsWith("--aout", StringComparison.Ordinal))
                ? options.Prepend("--aout=directsound,any").ToArray() : options;

        /// <summary>WPF's linear sample gain (0-1) as LibVLC's cubic 0-100 volume: 0.5 lands at about -6 dB.</summary>
        internal static int VlcVolume(float gain) => (int)Math.Round(Math.Cbrt(Math.Clamp(gain, 0f, 1f)) * 100);

        /// <summary>Ducking only, with pactl stubbed: for the state-machine test.</summary>
        internal LibVlcAudio(Func<string, string> pactl, string? duckJournal = null) { _vlc = null!; _pactl = pactl; _journal = duckJournal; }

        /// <summary>Ducking only, pactl stubbed and the rescan timer on <paramref name="clock"/> (AudioDuckRescanTests).</summary>
        internal LibVlcAudio(Func<string, string> pactl, TimeProvider clock, string? duckJournal = null)
            : this(pactl, duckJournal) { _duckClock = clock; }

        /// <summary>
        /// App start-up only (never the probes or tests, which must not touch a real profile): restore
        /// what a run that died while ducked left quiet, then journal every duck to <paramref name="path"/>
        /// (WPF AudioService RecoverFromCrash / SaveDuckingState). Linux only: this head ducks nothing on Windows.
        /// </summary>
        internal void StartDuckJournal(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows: WPF's WASAPI ducker, journalling WPF's ducking_recovery.json beside the pulse
                // journal's path (UserData); it queues behind DuckRecovery's launch restore.
                _win = WindowsDucker.ForApp(Path.GetDirectoryName(path)!);
                CoreAudio.DuckProvider = _win.Duck;
                CoreAudio.UnduckProvider = _win.Unduck;
                CoreAudio.DuckGenerationProvider = () => _win.Generation;
                return;
            }
            if (!OperatingSystem.IsLinux()) return;
            _journal = path;
            RecoverDucks();
        }

        /// <summary>The Windows ducker, once App start-up made it (never in probes or tests).</summary>
        private WindowsDucker? _win;

        /// <summary>On the pactl queue, so it lands before any new duck sweep journals.</summary>
        internal void RecoverDucks()
        {
            if (_journal is { } path) Enqueue(() => DuckRecovery.RecoverPulse(path, _pactl, Environment.ProcessId));
        }

        public void Seed()
        {
            Instance = this;
            LayeredAudio.Instance = new LayeredAudio(path => new LayeredAudio.VlcLayerPlayer(_vlc, path));
            CoreAudio.PlayOneShotProvider = (path, volume, tag, started, finished) => PlayOneShot(path, volume, tag, started, finished);
            CoreAudio.PlayStoppableProvider = PlayOneShot;
            // Windows ducking is seeded by StartDuckJournal (App start-up only): a probe or test must never
            // lower the desk's real apps.
            if (!OperatingSystem.IsLinux()) return;
            CoreAudio.DuckProvider = Duck;
            CoreAudio.UnduckProvider = Unduck;
            CoreAudio.DuckGenerationProvider = () => { lock (_duckLock) return _duckGeneration; };
        }

        /// <summary>Same contract as WPF's AudioService.PlayOneShot: onStarted gets the clip length
        /// once playing; onFinished fires exactly once - off the UI thread when the clip ends or
        /// errors, synchronously on the caller's thread when it is refused (muted, missing file).
        /// Returns the clip's stop (WPF handle.Stop): it ends the clip early, and onFinished still fires once.</summary>
        public Action PlayOneShot(string path, float volume, string tag, Action<TimeSpan>? onStarted, Action? onFinished)
        {
            if (volume <= 0f || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Fire(onFinished); return static () => { }; }

            var media = new Media(_vlc, path, FromType.FromPath);
            media.AddOption(NoVideo);
            var player = new MediaPlayer(media);
            // WPF's volume is linear sample gain; LibVLC's is cubic, so 0.5 must land at ~-6 dB, not -18.
            var vol = VlcVolume(volume);
            var done = 0;
            void Finish()
            {
                if (Interlocked.Exchange(ref done, 1) != 0) return;
                // Never dispose a player from inside its own libvlc event: that deadlocks.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { lock (player) player.Dispose(); media.Dispose(); } catch (Exception ex) { Log.Debug(ex, "[Audio] {Tag} dispose", tag); }
                    Fire(onFinished);
                });
            }
            player.Playing += (_, _) =>
            {
                // Setting volume before Play, or inside this libvlc event, is lost: the pulse
                // output only takes it once it exists, from a thread that is not libvlc's own.
                ThreadPool.QueueUserWorkItem(_ => { lock (player) if (done == 0) { ApplyPreferredDevice(player); player.Volume = vol; } });
                if (onStarted is null) return;
                try { onStarted(TimeSpan.FromMilliseconds(Math.Max(0, player.Length))); }
                catch (Exception ex) { Log.Debug(ex, "[Audio] {Tag}: onStarted threw", tag); }
            };
            player.EndReached += (_, _) => Finish();
            player.EncounteredError += (_, _) => Finish();
            player.Stopped += (_, _) => Finish();
            if (!player.Play()) Finish();
            // Off the caller's thread: libvlc's Stop must never run inside one of its own events.
            return () => ThreadPool.QueueUserWorkItem(_ => { lock (player) if (done == 0) player.Stop(); });
        }

        /// <summary>A clip the caller can re-volume and stop (mind wipe). A loop is WPF's overlap
        /// (MindWipeService.cs:55): the next copy starts 120 ms before the current one ends, because
        /// LibVLC's own <c>:input-repeat</c> reopens the input and left a measured 64 ms of silence
        /// at every restart.</summary>
        internal MindWipePlayer.IVoice? PlayVoice(string path, double volume, bool loop, Action onEnded)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            if (loop)
            {
                var l = new LoopVoice(this, path, onEnded) { Volume = volume };
                return l.Start() ? l : null;
            }
            var voice = NewVoice(path, onEnded);
            voice.Volume = volume;
            return voice.Play() ? voice : null;
        }

        // WPF QuizWindow.xaml.cs:1191-1227, using its sequential decoded loop (:41-54).
        internal static Action<MediaPlayer>? ConfigureQuizDrone;
        internal MindWipePlayer.IVoice? PlayQuizDrone(string path, double volume, Action onEnded)
        {
            var input = new QuizDroneInput(path);
            Media? media = null;
            var owned = false;
            try
            {
                media = new Media(_vlc, input, NoVideo, ":demux=rawaud", ":rawaud-fourcc=f32l",
                    $":rawaud-samplerate={input.SampleRate}", $":rawaud-channels={input.Channels}");
                var player = new MediaPlayer(media);
                var voice = new Voice(player, media, () => { input.Dispose(); onEnded(); });
                owned = true;
                try { ConfigureQuizDrone?.Invoke(player); voice.Volume = volume; return voice.Play() ? voice : null; }
                catch { voice.Dispose(); throw; }
            }
            catch { if (!owned) { media?.Dispose(); input.Dispose(); } throw; }
        }

        private Voice NewVoice(string path, Action onEnded)
        {
            var media = new Media(_vlc, path, FromType.FromPath);
            media.AddOption(NoVideo);
            return new Voice(new MediaPlayer(media), media, onEnded);
        }

        internal static Action<MediaPlayer> RouteVoice = ApplyPreferredDevice;

        private sealed class Voice(MediaPlayer player, Media media, Action onEnded) : MindWipePlayer.IVoice
        {
            private int _vol, _done;
            /// <summary>Raised once with the clip length in ms (0 when unknown) when it starts playing.</summary>
            internal Action<long>? Started;
            internal volatile bool Failed;
            public double Volume
            {
                // Cubic like PlayOneShot; applied off libvlc's thread once the output exists.
                set { _vol = (int)Math.Round(Math.Cbrt(Math.Clamp(value, 0, 1)) * 100); Apply(); }
            }
            private void Apply() => ThreadPool.QueueUserWorkItem(_ => { lock (player) if (_done == 0 && player.IsPlaying) { RouteVoice(player); player.Volume = _vol; } });
            public bool Play()
            {
                var started = 0;
                player.Playing += (_, _) =>
                {
                    Apply();
                    if (Interlocked.Exchange(ref started, 1) == 0) Fire(() => Started?.Invoke(Math.Max(0, player.Length)));
                };
                player.EndReached += (_, _) => Dispose();
                player.EncounteredError += (_, _) => { Failed = true; Dispose(); };
                if (player.Play()) return true;
                Failed = true;
                Dispose();
                return false;
            }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) != 0) return;
                // Never dispose a player from inside its own libvlc event: that deadlocks.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { lock (player) { player.Stop(); player.Dispose(); } media.Dispose(); }
                    catch (Exception ex) { Log.Debug(ex, "[Audio] mindwipe dispose"); }
                    Fire(onEnded);
                });
            }
        }

        /// <summary>Copies of one clip, each started <see cref="OverlapMs"/> before the previous ends.
        /// Ends (onEnded) when a copy fails and nothing else is playing.</summary>
        private sealed class LoopVoice(LibVlcAudio audio, string path, Action onEnded) : MindWipePlayer.IVoice
        {
            private const int OverlapMs = 120;   // WPF CROSSFADE_OVERLAP_SECONDS
            private readonly object _gate = new();
            private readonly List<Voice> _live = new();
            private Timer? _next;
            private bool _done;
            private double _vol;

            public double Volume { set { lock (_gate) { _vol = value; foreach (var v in _live) v.Volume = value; } } }

            public bool Start()
            {
                Voice v;
                lock (_gate)
                {
                    if (_done) return false;
                    v = null!;
                    v = audio.NewVoice(path, () => Retire(v));
                    v.Volume = _vol;
                    v.Started = Schedule;
                    _live.Add(v);
                }
                return v.Play();
            }

            /// <summary>WPF: interval = length - overlap, at least 100 ms. Unknown length: back to back.</summary>
            private void Schedule(long lengthMs)
            {
                if (lengthMs <= 0) return;
                lock (_gate)
                {
                    if (_done) return;
                    _next?.Dispose();
                    _next = new Timer(_ => Start(), null, Math.Max(100, lengthMs - OverlapMs), Timeout.Infinite);
                }
            }

            private void Retire(Voice v)
            {
                bool restart = false, dead = false;
                lock (_gate)
                {
                    _live.Remove(v);
                    if (_done || _live.Count > 0) return;
                    if (v.Failed) dead = _done = true; else restart = true;
                }
                if (dead) { Fire(onEnded); return; }
                if (restart && !Start()) { lock (_gate) _done = true; Fire(onEnded); }
            }

            public void Dispose()
            {
                Voice[] live;
                lock (_gate) { _done = true; _next?.Dispose(); _next = null; live = _live.ToArray(); _live.Clear(); }
                foreach (var v in live) v.Dispose();
                Fire(onEnded);
            }
        }

        /// <summary>Media option for audio players: skip any video track (an mp3's cover art would open a window).</summary>
        internal const string NoVideo = ":no-video";

        private static void Fire(Action? a) { try { a?.Invoke(); } catch { } }

        // ---- ducking (Linux, pactl) ----

        private readonly object _duckLock = new();
        private long _duckGeneration;
        private int _duckCount;
        private bool _isDucked;
        private readonly Dictionary<int, DuckRecovery.PulseStream> _originalVolumes = new(); // sink-input index -> owner + raw channel volumes
        private readonly Dictionary<int, DuckRecovery.PulseStream> _parked = new(); // restores that could not land (stream gone)
        private string? _journal; // ducking crash journal; null = none (tests, probes)
        private Task _duckWork = Task.CompletedTask; // chained so a duck and its unduck run in order
        private bool _pactlWarned;
        private Timer? _duckWatchdog; // WPF's safety net for a leaked duck ref: force-unduck after 5 min
        private ITimer? _duckRescan;
        private double _duckKeep;
        private long _duckEpoch; // a disposed timer's queued callback cannot enter the next duck window

        public void Duck(int strength)
        {
            if (CoreSettings.Current?.MasterVolume == 0) return; // as WPF: nothing of ours will play
            lock (_duckLock)
            {
                _duckCount++;
                if (_isDucked) return;
                _isDucked = true;
                _duckWatchdog = new Timer(_ => ForceUnduck(), null, 300_000, Timeout.Infinite);
                var keep = 1 - Math.Clamp(strength, 0, 100) / 100.0;
                _duckKeep = keep;
                var epoch = ++_duckEpoch;
                var interval = TimeSpan.FromMilliseconds(CoreAudio.DuckRescanIntervalMs);
                _duckRescan = _duckClock.CreateTimer(_ => RescanForNewSessions(epoch), null, interval, interval);
                // As WPF: our own layers are in-process, so the sweep skips them - duck them directly.
                LayeredAudio.Instance?.ApplyDuck((float)(1 - keep));
                Enqueue(() => DuckSweep(keep, epoch));
            }
        }

        public void Unduck(long generation)
        {
            lock (_duckLock)
            {
                if (generation >= 0 && generation != _duckGeneration) return; // stale callback
                _duckCount = Math.Max(0, _duckCount - 1);
                if (!_isDucked || _duckCount > 0) return;
                _isDucked = false;
                _duckWatchdog?.Dispose();
                _duckWatchdog = null;
                _duckRescan?.Dispose();
                _duckRescan = null;
                LayeredAudio.Instance?.ReleaseDuck();
                Enqueue(Restore);
            }
        }

        /// <summary>As WPF: advancing the generation turns every pending Unduck into a stale no-op.</summary>
        internal void ForceUnduck()
        {
            if (_win != null) { _win.ForceUnduck(); return; }
            lock (_duckLock)
            {
                _duckGeneration++;
                _duckCount = 1;
                Unduck(_duckGeneration);
            }
        }

        /// <summary>App exit: never leave other apps ducked behind us.</summary>
        internal void Shutdown()
        {
            if (_win != null) { _win.Shutdown(); return; }
            lock (_duckLock) { if (_isDucked) ForceUnduck(); }
            Drain();
            lock (_duckLock) { _retry?.Dispose(); _retry = null; }
        }

        internal void Drain() => _duckWork.Wait(2000);

        internal bool IsDucked { get { lock (_duckLock) return _isDucked; } }

        private void Enqueue(Action work) => _duckWork = _duckWork.ContinueWith(_ =>
        {
            try { work(); }
            catch (Exception ex)
            {
                if (_pactlWarned) return;
                _pactlWarned = true;
                Log.Warning("[Audio] ducking via pactl unavailable: {E}", ex.Message);
            }
        }, TaskScheduler.Default);

        /// <summary>WPF RescanForNewSessions: a slow endpoint must not grow a backlog past unduck.</summary>
        private void RescanForNewSessions(long epoch)
        {
            lock (_duckLock)
            {
                if (!_isDucked || epoch != _duckEpoch || !_duckWork.IsCompleted) return;
                var keep = _duckKeep;
                Enqueue(() => DuckSweep(keep, epoch, rescan: true));
            }
        }

        private void DuckSweep(double keep, long epoch, bool rescan = false)
        {
            try
            {
                foreach (var si in DuckRecovery.ParseSinkInputs(_pactl("-f json list sink-inputs")))
                {
                    if (si.Pid == Environment.ProcessId) continue;
                    // WPF AudioService.cs:1021-1041. WebKit audio runs in a child process.
                    if ((CoreSettings.Current?.ExcludeBambiCloudFromDucking ?? true)
                        && (si.Name.StartsWith("WebKit", StringComparison.Ordinal)
                            || si.Name.Contains("webview2", StringComparison.OrdinalIgnoreCase))) continue;
                    var index = si.Index;
                    // PulseAudio volume units: 65536 is unity. WPF skips <=0.001 on rescans so an
                    // already-silent/recycled session is not captured as its original volume.
                    if (rescan && si.Volumes.All(v => CoreAudio.IsSilentForDuckRescan(v / 65536f))) continue;
                    lock (_duckLock)
                    {
                        if (!_isDucked || epoch != _duckEpoch) return;
                        if (_originalVolumes.ContainsKey(index)) continue;
                        // A parked restore still owes this stream its TRUE original: keep that, never the live
                        // (still ducked) level, or the journal and the next unduck leave the app quieter.
                        _originalVolumes[index] = _parked.Remove(index, out var owed)
                            ? owed : new DuckRecovery.PulseStream(si.Pid, si.Name, si.Volumes.Select(v => v.ToString()).ToArray());
                    }
                    // Journal BEFORE lowering: a crash between the two must still find the original.
                    Journal();
                    try { _pactl($"set-sink-input-volume {index} {string.Join(' ', si.Volumes.Select(v => (int)(v * keep)))}"); }
                    catch { lock (_duckLock) _originalVolumes.Remove(index); } // the stream ended mid-sweep
                }
            }
            finally { Journal(); }
        }

        private void Restore()
        {
            KeyValuePair<int, DuckRecovery.PulseStream>[] saved;
            lock (_duckLock) { saved = _originalVolumes.ToArray(); }
            foreach (var (index, stream) in saved)
            {
                var ok = false;
                try { _pactl($"set-sink-input-volume {index} {string.Join(' ', stream.Volumes)}"); ok = true; }
                catch { } // the stream ended while ducked: pulse may remember the ducked level for that app
                lock (_duckLock)
                {
                    _originalVolumes.Remove(index);
                    // WPF parks what it could not hand back; the journal keeps it for the next launch.
                    if (!ok) _parked[index] = stream;
                }
            }
            lock (_duckLock)
                if (_parked.Count > 0)
                {
                    // WPF RestoreRetryTick: retry every 5 s for 3 min, as the app's stream comes back.
                    _retryDeadline = DateTime.UtcNow + RetryWindow;
                    _retry?.Dispose();
                    _retry = new Timer(_ => RetryTick(), null, RetryMs, RetryMs);
                }
            Journal();
        }

        internal const int RetryMs = 5_000;
        private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(3);
        private Timer? _retry;
        private DateTime _retryDeadline;

        internal bool RetryArmed { get { lock (_duckLock) return _retry != null; } }

        private void RetryTick()
        {
            lock (_duckLock)
            {
                if (_parked.Count == 0 || DateTime.UtcNow > _retryDeadline)
                {
                    // Stop retrying; what is left stays in the journal for the next launch.
                    _retry?.Dispose();
                    _retry = null;
                    return;
                }
            }
            Enqueue(RetryParked);
        }

        /// <summary>WPF RestoreRetrySweep: the parked restores on the live streams, same PID else same app by
        /// name, never a stream this run holds ducked right now.</summary>
        internal void RetryParked()
        {
            Dictionary<int, DuckRecovery.PulseStream> parked;
            HashSet<int> busy;
            lock (_duckLock)
            {
                if (_parked.Count == 0) return;
                parked = new(_parked);
                busy = new(_originalVolumes.Keys);
            }
            var landed = DuckRecovery.RestorePulse(parked, _pactl, Environment.ProcessId, busy);
            lock (_duckLock)
            {
                foreach (var index in landed) _parked.Remove(index);
                if (_parked.Count == 0) { _retry?.Dispose(); _retry = null; }
            }
            if (landed.Count > 0) Journal();
        }

        /// <summary>Every original not yet handed back (WPF SaveDuckingState via DuckRecoveryRule.ToPersist).</summary>
        private void Journal()
        {
            if (_journal is not { } path) return;
            Dictionary<int, DuckRecovery.PulseStream> owed;
            lock (_duckLock) owed = global::ConditioningControlPanel.Services.Audio.DuckRecoveryRule.ToPersist(_originalVolumes, _parked);
            DuckRecovery.SavePulse(path, owed);
        }

        // ---- output device (WPF AudioService.EnumerateOutputDevices / ApplyPreferredDevice) ----

        /// <summary>One picker entry. Id is the PulseAudio sink name, which is also what LibVLC's
        /// pulse output takes in SetOutputDevice; empty Id is the system default.</summary>
        internal sealed record OutputDevice(string Id, string Name)
        {
            public override string ToString() => Name;
        }

        /// <summary>As WPF: the first entry is always the synthetic system default.</summary>
        internal static List<OutputDevice> EnumerateOutputDevices(Func<string, string>? pactl = null)
        {
            var list = new List<OutputDevice> { new("", Loc.Get("set2_mic_system_default")) };
            if (pactl is null && OperatingSystem.IsWindows())
                return WindowsAudioDevices.Enumerate(list);
            if (pactl is null && !OperatingSystem.IsLinux()) return list;
            try
            {
                using var doc = JsonDocument.Parse((pactl ?? Pactl)("-f json list sinks"));
                foreach (var sink in doc.RootElement.EnumerateArray())
                {
                    var id = sink.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (id.Length == 0) continue;
                    var name = sink.TryGetProperty("description", out var d) ? d.GetString() : null;
                    list.Add(new(id, string.IsNullOrWhiteSpace(name) ? id : name));
                }
            }
            catch (Exception ex) { Log.Information("EnumerateOutputDevices: no pactl device list ({Error})", ex.Message); }
            return list;
        }

        /// <summary>WPF's ApplyPreferredDevice rule: the saved device, only when the live player
        /// lists it (a stale id routes audio to nowhere), and not again when already there.</summary>
        internal static string? PreferredDevice(string? saved, string? current, IEnumerable<string> available)
        {
            if (string.IsNullOrEmpty(saved) || saved == current) return null;
            var live = available.ToArray();
            if (live.Contains(saved)) return saved;
            // WPF AudioService.cs:216-269 validates the live output. This head uses DirectSound
            // for per-player gain: its device GUID is the suffix of the saved WASAPI endpoint ID.
            if (saved.StartsWith("{0.0.0.", StringComparison.Ordinal)
                && saved.LastIndexOf('{') is var start && start > 0
                && Guid.TryParse(saved.AsSpan(start), out var endpoint))
            {
                var id = live.FirstOrDefault(d => Guid.TryParse(d, out var guid) && guid == endpoint);
                return id == current ? null : id;
            }
            return null;
        }

        /// <summary>Call once the player is live (its Playing event), never before: like WPF's
        /// mmdevice, the output's device list is only meaningful once it exists.</summary>
        internal static void ApplyPreferredDevice(MediaPlayer player)
        {
            try
            {
                var id = PreferredDevice(CoreSettings.Current.AudioOutputDeviceId, player.OutputDevice,
                    player.AudioOutputDeviceEnum.Select(d => d.DeviceIdentifier));
                if (id != null) player.SetOutputDevice(id);
            }
            catch (Exception ex) { Log.Debug("ApplyPreferredDevice: {Error}", ex.Message); }
        }

        /// <summary>pactl is on PATH (pulseaudio-utils / libpulse). Checked once.</summary>
        internal static bool PactlInstalled => _pactlInstalled.Value;
        private static readonly Lazy<bool> _pactlInstalled = new(() => OnPath("pactl", Environment.GetEnvironmentVariable("PATH")));

        internal static bool OnPath(string exe, string? path) =>
            (path ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Any(d => File.Exists(System.IO.Path.Combine(d, exe)));

        internal static string Pactl(string args)
        {
            if (!PactlInstalled) throw new InvalidOperationException("pactl is not installed");
            using var p = Process.Start(new ProcessStartInfo("pactl", args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"pactl {args}: {p.StandardError.ReadToEnd().Trim()}");
            return output;
        }
    }
}
