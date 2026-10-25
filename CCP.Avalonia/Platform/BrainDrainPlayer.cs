using System;
using System.ComponentModel;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

// Audio only. ponytail: desktop capture/compositor remains unported; the panel says so.
internal sealed class BrainDrainPlayer
{
    internal static BrainDrainPlayer? Instance;
    private readonly Func<string, double, bool, Action, MindWipePlayer.IVoice?> _play;
    private readonly TimeProvider _time;
    private readonly Func<double> _roll;
    private readonly object _gate = new();
    private ITimer? _timer;
    private MindWipePlayer.IVoice? _voice;
    private bool _busy, _running;
    private long _generation;
    private AppSettings? _settings;
    internal BrainDrainPlayer(Func<string, double, bool, Action, MindWipePlayer.IVoice?> play,
        TimeProvider? time = null, Func<double>? roll = null)
    { _play = play; _time = time ?? TimeProvider.System; _roll = roll ?? Random.Shared.NextDouble; }
    internal bool IsRunning { get { lock (_gate) return _running; } }
    internal void Start()
    {
        lock (_gate)
        {
            if (_running || !CoreSettings.Current.BrainDrainEnabled) return;
            _running = true;
            _settings = CoreSettings.Current;
            _settings.PropertyChanged += Changed;
            var interval = BrainDrainAudioPolicy.Interval(_settings.BrainDrainHighRefresh);
            _timer = _time.CreateTimer(_ => Tick(interval), null, interval, interval);
        }
        App.DiscordRpc?.SetBrainDrainActivity();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.MasterVolume) or nameof(AppSettings.BrainDrainVolume))
            lock (_gate) if (_voice != null) _voice.Volume = Gain();
    }
    private static double Gain() => BrainDrainAudioPolicy.Volume(CoreSettings.Current.MasterVolume, CoreSettings.Current.BrainDrainVolume);
    internal void Tick(TimeSpan interval)
    {
        long generation;
        lock (_gate)
        {
            if (!_running || _busy || CoreAudio.IsOutputSuppressed || _roll() >= BrainDrainAudioPolicy.Probability(CoreSettings.Current.BrainDrainIntensity, interval)) return;
            _busy = true; generation = ++_generation;
        }
        MindWipePlayer.IVoice? voice = null;
        try
        {
            var clips = BrainDrainAudioPolicy.Discover(BrainDrainAudioPolicy.AudioFolder, BrainDrainAudioPolicy.LegacyFolder);
            if (clips.Length != 0) voice = _play(clips[Random.Shared.Next(clips.Length)], Gain(), false, () =>
            {
                lock (_gate) if (generation == _generation) { _voice = null; _busy = false; ++_generation; }
            });
            lock (_gate)
            {
                if (_running && generation == _generation)
                { _voice = voice; _busy = voice != null; if (voice != null) voice.Volume = Gain(); voice = null; }
            }
        }
        catch { lock (_gate) if (generation == _generation) _busy = false; }
        finally { voice?.Dispose(); }
    }
    internal void Stop()
    {
        MindWipePlayer.IVoice? voice;
        lock (_gate)
        {
            _running = _busy = false; ++_generation;
            _timer?.Dispose(); _timer = null;
            if (_settings != null) _settings.PropertyChanged -= Changed;
            _settings = null; voice = _voice; _voice = null;
        }
        voice?.Dispose();
    }
}
