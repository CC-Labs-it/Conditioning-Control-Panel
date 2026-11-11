using System;
using System.ComponentModel;
using ConditioningControlPanel.Models;
namespace ConditioningControlPanel.Services.Audio;
/// <summary>Follows <see cref="AppSettings.MasterVolume"/> across settings replacement (WPF Services/Audio/MasterVolumeWatch.cs).</summary>
public sealed class MasterVolumeBinding : IDisposable
{
    private readonly SettingsService? _service;
    private readonly Action<int> _apply;
    private readonly object _gate = new();
    private AppSettings? _settings;
    private volatile bool _disposed;
    public MasterVolumeBinding(Action<int> apply, SettingsService? service = null)
    {
        _apply = apply;
        _service = service ?? CoreSettings.Service;
        // Subscribe before reading Current, so a replacement in between is always seen.
        if (_service != null) _service.CurrentReplaced += Replaced;
        Replaced();
    }
    public int Level => _settings!.MasterVolume;
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.MasterVolume)) Apply();
    }
    private void Replaced()
    {
        lock (_gate)
        {
            if (_disposed) return;   // an in-flight replacement must not reattach a disposed binding
            if (_settings != null) _settings.PropertyChanged -= Changed;
            (_settings = _service?.Current ?? CoreSettings.Current).PropertyChanged += Changed;
        }
        Apply();
    }
    private void Apply() { if (!_disposed) { try { _apply(Level); } catch { /* A retired voice must not block other players. */ } } }
    public void Dispose()
    {
        if (_service != null) _service.CurrentReplaced -= Replaced;
        lock (_gate) { _disposed = true; if (_settings != null) _settings.PropertyChanged -= Changed; }
    }
    /// <summary>WPF MindWipeService.EffectiveVolume (MindWipeService.cs:118).</summary>
    public static float MindWipeGain(int master, double own) => double.IsNaN(own) ? 0f
        : Math.Clamp(master, 0, 100) / 100f * (float)Math.Clamp(own, 0, 1);
}
