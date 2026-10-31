using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Models;
namespace ConditioningControlPanel.Avalonia.Platform;
// WPF Services/LockCard/BrainDrainService.cs:138-149,155-207,210-290,297-309,325-411.
// Reuses the head's stoppable LibVLC voice. No decoder work runs on the UI thread.
internal sealed class BrainDrainPlayer
{
    internal static BrainDrainPlayer? Instance;
    private readonly Func<string, double, bool, Action, MindWipePlayer.IVoice?> _create;
    private readonly TimeProvider _time;
    private readonly Func<double> _roll;
    private readonly Random _random = new();
    private readonly object _gate = new();
    private string[] _clips = [];
    private ITimer? _timer;
    private MindWipePlayer.IVoice? _voice;
    private bool _running, _building;
    private int _generation;
    private AppSettings? _settings;
    internal bool IsRunning { get { lock (_gate) return _running; } }
    internal int ClipCount { get { lock (_gate) return _clips.Length; } }
    internal static string Folder => Path.Combine(CorePaths.EffectiveAssets, "braindrain");
    internal static string LegacyFolder => Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "braindrain");
    internal static string EnsureFolder() { Directory.CreateDirectory(Folder); return Folder; }
    internal BrainDrainPlayer(Func<string, double, bool, Action, MindWipePlayer.IVoice?> create,
        TimeProvider? time = null, Func<double>? roll = null) {
        _create = create; _time = time ?? TimeProvider.System; _roll = roll ?? _random.NextDouble;
    }
    internal void Seed() {
        Instance = this;
        CoreEngine.StartBrainDrain = Start;
        CoreEngine.StopBrainDrain = Stop;
    }
    private static IEnumerable<string> Clips(string folder) {
        try { return Directory.GetFiles(folder).Where(p => new[] { ".mp3", ".wav", ".ogg" }
            .Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToArray(); }
        catch { return []; }
    }
    internal void Reload() {
        try { EnsureFolder(); } catch { }
        var files = Clips(Folder).Concat(Clips(LegacyFolder))
            .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        lock (_gate) _clips = files;
    }
    internal void Start() {
        lock (_gate) {
            if (_running || !CoreSettings.Current.BrainDrainEnabled) return;
            Reload(); _settings = CoreSettings.Current;
            _settings.PropertyChanged += SettingsChanged;
            _running = true;
            var every = Interval; _timer = _time.CreateTimer(_ => Tick(), null, every, every);
            App.DiscordRpc?.SetBrainDrainActivity(); // WPF BrainDrainService.cs:240
        }
    }
    private TimeSpan Interval => TimeSpan.FromMilliseconds(CoreSettings.Current.BrainDrainHighRefresh ? 500 : 5000);
    private static double Volume => Math.Clamp(CoreSettings.Current.MasterVolume, 0, 100) / 100.0
        * Math.Clamp(CoreSettings.Current.BrainDrainVolume, 0, 100) / 100.0;
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e) {
        lock (_gate) {
            if (e.PropertyName is nameof(AppSettings.MasterVolume) or nameof(AppSettings.BrainDrainVolume))
                if (_voice != null) _voice.Volume = Volume; // WPF BrainDrainService.cs:435-441
        }
    }
    internal void Stop() {
        MindWipePlayer.IVoice? voice;
        ITimer? timer;
        bool wasRunning;
        lock (_gate) {
            wasRunning = _running;
            _running = false; ++_generation;
            voice = _voice; _voice = null;
            timer = _timer; _timer = null;
            if (_settings != null) _settings.PropertyChanged -= SettingsChanged;
            _settings = null;
        }
        timer?.Dispose();
        voice?.Dispose();
        if (wasRunning) App.DiscordRpc?.SetIdleActivity(); // WPF BrainDrainService.cs:250-270, audio first
    }
    internal void Tick() {
        string path; int generation;
        lock (_gate) {
            if (!_running || _building || _voice != null || _clips.Length == 0) return;
            var probability = Math.Clamp(CoreSettings.Current.BrainDrainIntensity, 0, 100) / 100.0
                / (60 / Interval.TotalSeconds); // WPF BrainDrainService.cs:285
            if (_roll() >= probability) return;
            path = _clips[_random.Next(_clips.Length)];
            generation = _generation; _building = true;
        }
        MindWipePlayer.IVoice? voice = null;
        bool ended = false, published = false;
        try
        {
            voice = _create(path, Volume, false, () =>
            {
                lock (_gate) {
                    ended = true;
                    if (ReferenceEquals(_voice, voice)) _voice = null;
                }
            });
            lock (_gate) {
                // WPF BrainDrainService.cs:383-397: Stop and the actual start share this lock.
                if (_running && generation == _generation && !ended && voice != null)
                {
                    voice.Volume = Volume;
                    if (voice.Play() && !ended) { _voice = voice; published = true; }
                }
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain playback failed"); }
        finally
        {
            lock (_gate) _building = false;
            if (!published) voice?.Dispose(); // A stop during decoder creation cannot publish a late voice.
        }
    }
}
