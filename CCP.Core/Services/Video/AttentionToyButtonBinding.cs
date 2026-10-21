using System;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services;

/// <summary>WPF SpawnTarget's alternative input, owned by one visible attention batch.
/// Dispose on a catch, expiry or teardown; an event already queued after disposal is harmless.</summary>
public sealed class AttentionToyButtonBinding : IDisposable
{
    private HapticService? _service;
    private readonly Func<bool> _isLive;
    private readonly Action _hit;

    private AttentionToyButtonBinding(HapticService service, Func<bool> isLive, Action hit)
    {
        _service = service;
        _isLive = isLive;
        _hit = hit;
        service.ToyInput.ButtonPressed += OnPress;
    }

    public static AttentionToyButtonBinding? Arm(HapticService? service, Func<bool> isLive, Action hit) =>
        service is { Settings.AttentionCheckToyButton: true, Settings.ToyInputEnabled: true }
            ? new(service, isLive, hit) : null;

    private void OnPress(object? sender, HapticToyEvent e)
    {
        var service = _service;
        if (service == null) return;
        if (!_isLive()) { Dispose(); return; }
        Dispose(); // one reward per batch, even if the hit callback causes another input event
        Serilog.Log.Information("ATTENTION: satisfied by toy button press");
        _hit();
        _ = service.PostEvent(HapticEventKind.ToyButtonReward);
    }

    public void Dispose()
    {
        var service = _service;
        _service = null;
        if (service != null) service.ToyInput.ButtonPressed -= OnPress;
    }
}
