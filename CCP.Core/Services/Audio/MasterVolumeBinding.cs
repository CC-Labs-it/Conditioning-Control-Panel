using System;
using System.ComponentModel;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Audio;

/// <summary>Follows the master setting, including settings replacement. Dispose with the player.</summary>
public sealed class MasterVolumeBinding : IDisposable
{
    private readonly SettingsService? _service;
    private readonly Action<int> _apply;
    private AppSettings _settings;
    private bool _disposed;

    public MasterVolumeBinding(Action<int> apply, SettingsService? service = null)
    {
        _apply = apply;
        _service = service ?? CoreSettings.Service;
        _settings = _service?.Current ?? CoreSettings.Current;
        _settings.PropertyChanged += Changed;
        if (_service != null) _service.CurrentReplaced += Replaced;
        Apply();
    }

    public int Level => _settings.MasterVolume;

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.MasterVolume)) Apply();
    }

    private void Replaced()
    {
        _settings.PropertyChanged -= Changed;
        _settings = _service!.Current;
        _settings.PropertyChanged += Changed;
        Apply();
    }

    private void Apply()
    {
        if (!_disposed) { try { _apply(Level); } catch { /* A retired voice must not block other players. */ } }
    }

    public void Dispose()
    {
        _disposed = true;
        _settings.PropertyChanged -= Changed;
        if (_service != null) _service.CurrentReplaced -= Replaced;
    }

    public static float MindWipeGain(int master, double own) => double.IsNaN(own) ? 0f
        : Math.Clamp(master, 0, 100) / 100f * (float)Math.Clamp(own, 0, 1);

    public static float SoundGain(int own, int master)
    {
        var level = Math.Clamp(own, 0, 100) / 100f * (Math.Clamp(master, 0, 100) / 100f);
        return level <= 0 ? 0 : Math.Max(0.05f, (float)Math.Pow(level, 1.5));
    }
}
