using System;
using System.ComponentModel;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

// Audio only. Desktop capture, melt and Studio controls remain a separate port.
// WPF Services/LockCard/BrainDrainService.cs:138-146,280-306,417-432.
internal sealed class BrainDrainPlayer
{
    internal static BrainDrainPlayer? Instance;
    private readonly Func<string, Action, LibVlcAudio.IPreparedVoice?> _prepare;
    private readonly TimeProvider _time;
    private readonly Func<double> _roll;
    private readonly object _gate = new();
    private ITimer? _timer;
    private MindWipePlayer.IVoice? _voice;
    private bool _busy, _running;
    private long _generation;
    private AppSettings? _settings;
    private SettingsService? _service;
    internal BrainDrainPlayer(Func<string, Action, LibVlcAudio.IPreparedVoice?> prepare,
        TimeProvider? time = null, Func<double>? roll = null)
    { _prepare = prepare; _time = time ?? TimeProvider.System; _roll = roll ?? Random.Shared.NextDouble; }
    internal bool IsRunning { get { lock (_gate) return _running; } }
    internal void Start()
    {
        lock (_gate)
        {
            if (_running || !CoreSettings.Current.BrainDrainEnabled) return;
            _running = true;
            _settings = CoreSettings.Current;
            _settings.PropertyChanged += Changed;
            _service = CoreSettings.Service;
            if (_service != null) _service.CurrentReplaced += Replaced;
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
    // A cloud restore/reset swaps the settings object: follow it so live gain changes still land.
    private void Replaced()
    {
        lock (_gate)
        {
            if (!_running) return;
            if (_settings != null) _settings.PropertyChanged -= Changed;
            _settings = CoreSettings.Current; _settings.PropertyChanged += Changed;
            if (_voice != null) _voice.Volume = Gain();
        }
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
        LibVlcAudio.IPreparedVoice? voice = null;
        try
        {
            var clips = BrainDrainAudioPolicy.Discover(BrainDrainAudioPolicy.AudioFolder, BrainDrainAudioPolicy.LegacyFolder);
            if (clips.Length != 0) voice = _prepare(clips[Random.Shared.Next(clips.Length)], () =>
            {
                lock (_gate) if (generation == _generation) { _voice = null; _busy = false; ++_generation; }
            });
            lock (_gate)
            {
                // WPF BrainDrainService.cs:377-388: decoder creation may outlive panic. Check
                // and start under the same gate as Stop, so a stopped generation stays silent.
                if (_running && generation == _generation && voice != null)
                {
                    voice.Volume = Gain();
                    if (voice.Play() && _running && generation == _generation)
                    { _voice = voice; voice = null; }
                }
                if (generation == _generation) _busy = _voice != null;
            }
        }
        catch { lock (_gate) if (generation == _generation) _busy = false; }
        finally { voice?.Dispose(); }
    }
    internal void Stop()
    {
        MindWipePlayer.IVoice? voice; bool wasRunning;
        lock (_gate)
        {
            wasRunning = _running;
            _running = _busy = false; ++_generation;
            _timer?.Dispose(); _timer = null;
            if (_settings != null) _settings.PropertyChanged -= Changed;
            if (_service != null) _service.CurrentReplaced -= Replaced;
            _settings = null; _service = null; voice = _voice; _voice = null;
        }
        voice?.Dispose();
        if (wasRunning) App.DiscordRpc?.SetIdleActivity();   // WPF LockCard/BrainDrainService.cs:270, as MindWipePlayer.cs:105
    }
}
