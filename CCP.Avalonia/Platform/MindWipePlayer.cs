using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The Avalonia head's mind-wipe service: WPF <c>Services/LockCard/MindWipeService.cs</c> over
    /// <see cref="CoreMindWipe"/>. The deciding half (tick interval, per-tick probability, clip
    /// discovery) is <see cref="MindWipeSchedule"/> in Core; this is the playing half.
    ///
    /// <para>Loop voices overlap by 120 ms, as in WPF.</para>
    /// </summary>
    internal sealed class MindWipePlayer
    {
        /// <summary>A prepared or playing clip; Play starts it, volume is 0..1.</summary>
        internal interface IVoice : IDisposable { double Volume { set; } bool Play(); }

        private readonly Func<string, double, bool, Action, IVoice?> _create;
        private readonly TimeProvider _time;
        private readonly Func<double> _roll;
        private readonly Random _random = new();
        private readonly object _lock = new();
        private string[] _clips = Array.Empty<string>();
        private ITimer? _tick, _cleanSlate;
        private IVoice? _oneShot, _loop;
        private bool _running, _sessionMode;
        private long _loopGeneration;
        private int _sessionBase;
        private DateTime _sessionStart;
        private double _frequency = 6, _volume = 0.5;

        /// <summary>Raised with the loop's length in seconds once it has played 60 s (WPF Clean Slate).</summary>
        internal Action<double>? CleanSlate;

        /// <param name="create">Creates a silent voice, or returns null when it cannot.</param>
        internal MindWipePlayer(Func<string, double, bool, Action, IVoice?> create, TimeProvider? time = null, Func<double>? roll = null)
        {
            _create = create;
            _time = time ?? TimeProvider.System;
            _roll = roll ?? _random.NextDouble;
            ReloadClips();
        }

        internal bool IsRunning { get { lock (_lock) return _running; } }
        internal bool IsLooping { get { lock (_lock) return _loop != null; } }
        internal int ClipCount { get { lock (_lock) return _clips.Length; } }

        /// <summary>Seeds every <see cref="CoreMindWipe"/> provider with this instance.</summary>
        internal void Seed()
        {
            CoreMindWipe.StartProvider = Start;
            CoreMindWipe.StartSessionProvider = StartSession;
            CoreMindWipe.StopProvider = Stop;
            CoreMindWipe.IsRunningProvider = () => IsRunning;
            CoreMindWipe.TriggerOnceProvider = TriggerOnce;
            CoreMindWipe.StartLoopProvider = StartLoop;
            CoreMindWipe.StopLoopProvider = StopLoop;
            CoreMindWipe.IsLoopingProvider = () => IsLooping;
            CoreMindWipe.UpdateSettingsProvider = UpdateSettings;
            CoreMindWipe.ReloadClipsProvider = ReloadClips;
            CoreMindWipe.ClipCountProvider = () => ClipCount;
        }

        internal void ReloadClips()
        {
            var clips = MindWipeSchedule.DiscoverClips(CoreSettings.Current.MindWipeAudioPath);
            lock (_lock) _clips = clips;
        }

        internal void Start(double frequencyPerHour, double volume)
        {
            bool started = false;
            lock (_lock)
            {
                if (!_running)
                {
                    _running = started = true;
                    _sessionMode = false;
                    var every = MindWipeSchedule.TickInterval;
                    _tick = _time.CreateTimer(_ => Tick(), null, every, every);
                }
            }
            UpdateSettings(frequencyPerHour, volume);
            if (started) App.DiscordRpc?.SetMindWipeActivity();   // WPF MindWipeService.cs:179
            Log.Information("MindWipe: Started (frequency: {Freq}/hour, volume: {Vol}%, files: {Count})",
                frequencyPerHour, volume * 100, ClipCount);
        }

        /// <summary>WPF StartSession (MindWipeService.cs:232-248): escalating per five-minute block from now.
        /// The session sets the volume first (SessionEngine.cs:218); a running service ignores it all.</summary>
        internal void StartSession(int baseMultiplier, double volume)
        {
            lock (_lock)
            {
                if (_running) return;
                _running = _sessionMode = true;
                _sessionBase = baseMultiplier;
                _sessionStart = _time.GetLocalNow().DateTime;
                _volume = MindWipeSchedule.ClampVolume(volume);
                var every = MindWipeSchedule.TickInterval;
                _tick = _time.CreateTimer(_ => Tick(), null, every, every);
            }
            App.DiscordRpc?.SetMindWipeActivity();   // WPF MindWipeService.cs:245
            Log.Information("MindWipe: Started in session mode (base multiplier: {Base})", baseMultiplier);
        }

        /// <summary>Everything off, running or not: panic reaches a test clip played with the service stopped.</summary>
        internal void Stop()
        {
            IVoice? oneShot;
            ITimer? tick;
            bool wasRunning;
            lock (_lock)
            {
                wasRunning = _running;
                _running = false;
                oneShot = _oneShot; _oneShot = null;
                tick = _tick; _tick = null;
            }
            tick?.Dispose();
            oneShot?.Dispose();
            StopLoop();
            if (wasRunning) App.DiscordRpc?.SetIdleActivity();   // WPF MindWipeService.cs:230, after the audio teardown
        }

        internal void UpdateSettings(double frequencyPerHour, double volume)
        {
            lock (_lock)
            {
                _frequency = MindWipeSchedule.ClampFrequency(frequencyPerHour);
                _volume = MindWipeSchedule.ClampVolume(volume);
                if (_oneShot != null) _oneShot.Volume = _volume;
                if (_loop != null) _loop.Volume = _volume;
            }
        }

        /// <summary>WPF Timer_Tick: no random clips over a loop.</summary>
        private void Tick()
        {
            lock (_lock)
            {
                if (!_running || _loop != null || _clips.Length == 0) return;
                // WPF MindWipeService.cs:238,717: local wall time, including backward/DST jumps.
                var probability = _sessionMode
                    ? MindWipeSchedule.SessionProbability(_sessionBase, _time.GetLocalNow().DateTime - _sessionStart)
                    : MindWipeSchedule.Probability(_frequency);
                if (_roll() >= probability) return;
            }
            PlayOnce();
        }

        /// <summary>WPF TriggerOnce: plays at the saved volume, with the service stopped too.</summary>
        internal void TriggerOnce()
        {
            lock (_lock) _volume = MindWipeSchedule.ClampVolume(CoreSettings.Current.MindWipeVolume / 100.0);
            PlayOnce();
        }

        private void PlayOnce()
        {
            string clip;
            double volume;
            lock (_lock)
            {
                if (_clips.Length == 0) return;
                clip = _clips[_random.Next(_clips.Length)];
                volume = _volume;
            }
            IVoice? voice = null;
            voice = _create(clip, volume, false, () => { lock (_lock) if (ReferenceEquals(_oneShot, voice)) _oneShot = null; });
            if (voice == null) return;
            if (!voice.Play()) { voice.Dispose(); return; }
            IVoice? displaced;
            lock (_lock) { displaced = _oneShot; _oneShot = voice; }
            displaced?.Dispose();
            Log.Debug("MindWipe: Playing {File} at volume {Vol}%", Path.GetFileName(clip), volume * 100);
        }

        internal void StartLoop(double volume)
        {
            string? clip;
            long generation;
            IVoice? old;
            ITimer? oldCheck;
            lock (_lock)
            {
                generation = ++_loopGeneration;
                old = _loop; _loop = null;
                oldCheck = _cleanSlate; _cleanSlate = null;
                clip = _clips.Length == 0 ? null : _clips[_random.Next(_clips.Length)];
                _volume = volume = MindWipeSchedule.ClampVolume(volume);
            }
            oldCheck?.Dispose();
            old?.Dispose();
            if (clip == null) { Log.Warning("MindWipe: No audio files available for loop"); return; }

            IVoice? voice = null;
            voice = _create(clip, volume, true, () =>
            {
                ITimer? dead = null;
                lock (_lock) if (ReferenceEquals(_loop, voice)) { _loop = null; dead = _cleanSlate; _cleanSlate = null; }
                dead?.Dispose();
            });
            if (voice == null) return;
            bool started = false;
            lock (_lock)
            {
                if (generation == _loopGeneration)
                {
                    // Publish before Play queues the first build. Stop can now dispose it even
                    // while that build is blocked, and generation rejects an older factory result.
                    _loop = voice;
                    voice.Volume = _volume;
                    var start = _time.GetTimestamp();
                    _cleanSlate = _time.CreateTimer(_ =>
                    {
                        lock (_lock) if (!ReferenceEquals(_loop, voice)) return;
                        CleanSlate?.Invoke(_time.GetElapsedTime(start).TotalSeconds);
                    }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
                    started = voice.Play();
                    if (!started) { _loop = null; _cleanSlate?.Dispose(); _cleanSlate = null; }
                }
            }
            if (!started) { voice.Dispose(); return; }
            Log.Information("MindWipe: Loop started with {File} at {Vol}% volume", Path.GetFileName(clip), volume * 100);
        }

        internal void StopLoop()
        {
            IVoice? loop;
            ITimer? check;
            lock (_lock) { ++_loopGeneration; loop = _loop; _loop = null; check = _cleanSlate; _cleanSlate = null; }
            check?.Dispose();
            loop?.Dispose();
        }
    }
}
