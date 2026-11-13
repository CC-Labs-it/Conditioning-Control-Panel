using System;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.BackRoom;

// WPF BackRoomFxServices.GifRain and StopAll: ownership is local to this sink.
internal sealed class BackRoomRainBridge
{
    private readonly Action<double, double, double, double, double, double> _show;
    private readonly Action _close;
    private readonly Func<bool> _raining, _chaos;
    private readonly Func<long> _clock;
    private long _rainAt = long.MinValue / 2;

    internal BackRoomRainBridge(
        Action<double, double, double, double, double, double>? show = null,
        Action? close = null, Func<bool>? raining = null, Func<bool>? chaos = null, Func<long>? clock = null)
    {
        _show = show ?? ChaosGifCascadeOverlay.Show;
        _close = close ?? ChaosGifCascadeOverlay.CloseActive;
        _raining = raining ?? (() => ChaosGifCascadeOverlay.IsRaining);
        _chaos = chaos ?? (() => ChaosRunHost.IsActive);
        _clock = clock ?? (() => Environment.TickCount64);
    }

    internal void GifRain(int count, int durationMs, double opacity)
    {
        if (_raining()) return;
        _rainAt = _clock();
        double seconds = Math.Max(1.0, durationMs / 1000.0);
        _show(Math.Max(1, count) / seconds, seconds, GifCascadePayload.GIF_SIZE,
            GifCascadePayload.FALL_SPEED, opacity, GifCascadePayload.START_SCALE);
    }

    internal void StopAll()
    {
        if (_clock() - _rainAt < 12_000 && !_chaos()) _close();
        _rainAt = long.MinValue / 2;
    }
}
