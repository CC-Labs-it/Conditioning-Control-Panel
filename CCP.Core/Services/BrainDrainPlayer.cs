using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Brain Drain's audio half: WPF Services/LockCard/BrainDrainService.cs over the
    /// <see cref="CoreBrainDrain"/> seam. A timer (5 s, 500 ms in high refresh) rolls
    /// intensity% per minute; a winning roll plays one random clip from assets/braindrain at
    /// master x Brain Drain volume. A clip always plays to its end (#983): a roll mid-clip is skipped.
    /// Plays through the same LibVLC voices as Mind Wipe.
    /// </summary>
    public sealed class BrainDrainPlayer
    {
        // The head supplies only stop and live gain; Core never sees a native player.
        public sealed class Voice(Action stop, Action<double> volume) : IDisposable
        {
            public double Volume { set => volume(value); }
            public void Dispose() => stop();
        }

        private readonly Func<string, double, bool, Action, Voice?> _play;
        private readonly TimeProvider _time;
        private readonly Func<double> _roll;
        private readonly Random _random = new();
        private readonly object _lock = new();
        private string[] _clips = Array.Empty<string>();
        private ITimer? _tick;
        private Voice? _voice;
        private bool _running;
        private long _generation;
        private bool _building;
        private double _intensity = 50;
        private TimeSpan _interval = BrainDrainSchedule.TickInterval(false);

        public BrainDrainPlayer(Func<string, double, bool, Action, Voice?> play,
            TimeProvider? time = null, Func<double>? roll = null)
        {
            _play = play;
            _time = time ?? TimeProvider.System;
            _roll = roll ?? _random.NextDouble;
            ReloadClips();
        }

        /// <summary>WPF Start/Stop Discord presence (BrainDrainService.cs:240, :270); the head sets these.</summary>
        public Action? Started { get; init; }
        public Action? Stopped { get; init; }

        public bool IsRunning { get { lock (_lock) return _running; } }
        public int ClipCount { get { lock (_lock) return _clips.Length; } }
        public double Intensity { get { lock (_lock) return _intensity; } set { lock (_lock) _intensity = BrainDrainSchedule.ClampIntensity(value); } }

        public void Seed()
        {
            CoreBrainDrain.StartProvider = Start;
            CoreBrainDrain.StopProvider = Stop;
            CoreBrainDrain.IsRunningProvider = () => IsRunning;
            CoreBrainDrain.IntensityProvider = v => Intensity = v;
            CoreBrainDrain.ClipCountProvider = () => ClipCount;
            CoreBrainDrain.ReloadClipsProvider = ReloadClips;
            CoreBrainDrain.RefreshVolumeProvider = RefreshVolume;
        }

        /// <summary>WPF RefreshVolume (#1104): the clip that is playing right now takes the new
        /// master x Brain Drain volume; with nothing playing the next clip reads it anyway.</summary>
        public void RefreshVolume()
        {
            Voice? voice;
            lock (_lock) voice = _voice;
            if (voice == null) return;
            var s = CoreSettings.Current;
            try { voice.Volume = BrainDrainSchedule.EffectiveVolume(s.MasterVolume, s.BrainDrainVolume); }
            catch (Exception ex) { Log.Debug("BrainDrain: volume refresh failed: {E}", ex.Message); }
        }

        public void ReloadClips()
        {
            var clips = BrainDrainSchedule.DiscoverClips();
            lock (_lock) _clips = clips;
        }

        /// <summary>WPF Start: gated on BrainDrainEnabled, rescans the folder every start.</summary>
        public void Start()
        {
            var s = CoreSettings.Current;
            if (!s.BrainDrainEnabled) { Log.Debug("BrainDrain: Not enabled in settings"); return; }
            long generation;
            lock (_lock) { if (_running) return; generation = _generation; }
            ReloadClips();
            lock (_lock)
            {
                if (_running || generation != _generation) return;
                _intensity = BrainDrainSchedule.ClampIntensity(s.BrainDrainIntensity);   // WPF UpdateSettings
                _interval = BrainDrainSchedule.TickInterval(s.BrainDrainHighRefresh);
                _running = true;
                _tick = _time.CreateTimer(_ => Tick(), null, _interval, _interval);
            }
            try { Started?.Invoke(); } catch (Exception ex) { Log.Debug("BrainDrain: start hook failed: {E}", ex.Message); }
            Log.Information("BrainDrain started at intensity {Intensity}%, mode: {Mode}", _intensity,
                s.BrainDrainHighRefresh ? "High Refresh (500ms)" : "Normal (5s)");
        }

        /// <summary>WPF Stop: audio teardown first and unconditional (panic relies on it).</summary>
        public void Stop()
        {
            Voice? voice;
            ITimer? tick;
            bool wasRunning;
            lock (_lock)
            {
                wasRunning = _running;
                _running = false;
                ++_generation;
                _building = false;
                voice = _voice; _voice = null;
                tick = _tick; _tick = null;
            }
            try { voice?.Dispose(); }
            finally { tick?.Dispose(); }
            if (!wasRunning) return;
            try { Stopped?.Invoke(); } catch (Exception ex) { Log.Debug("BrainDrain: stop hook failed: {E}", ex.Message); }
        }

        public void Tick()
        {
            string clip;
            long generation;
            lock (_lock)
            {
                if (!_running || _clips.Length == 0 || _voice != null || _building) return;
                if (_roll() >= BrainDrainSchedule.Probability(_intensity, _interval)) return;
                if (CoreAudio.IsOutputSuppressed) return;
                clip = _clips[_random.Next(_clips.Length)];
                _building = true;
                generation = ++_generation;
            }
            Voice? voice = null;
            var ended = false;
            try
            {
                var s = CoreSettings.Current;
                voice = _play(clip, BrainDrainSchedule.EffectiveVolume(s.MasterVolume, s.BrainDrainVolume), false, () =>
                {
                    lock (_lock)
                    {
                        ended = true;
                        if (generation == _generation) _voice = null;
                    }
                });
                lock (_lock)
                {
                    if (generation == _generation)
                    {
                        _building = false;
                        if (_running && !ended)
                        {
                            _voice = voice;
                            voice = null;
                            RefreshVolume();
                        }
                    }
                }
                voice?.Dispose(); // stop won, including Stop followed by a fresh Start
            }
            catch (Exception ex)
            {
                voice?.Dispose();
                lock (_lock) if (generation == _generation) _building = false;
                Log.Warning(ex, "BrainDrain: playback failed");
            }
        }
    }
}
