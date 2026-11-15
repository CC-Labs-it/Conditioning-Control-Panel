using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosModeService.cs:1043-1062,2196-2262: the run owns the request before it reaches the screen.
internal sealed class ChaosVideoGuard(MandatoryVideoScheduler video, Func<DateTime> clock, Action endured)
{
    private int _token;
    private DateTime _cap;
    private bool _started;
    internal bool Armed => _token != 0;

    internal int Arm()
    {
        _token = video.ClaimChaosVideoToken();
        _cap = clock().AddSeconds(VideoPayload.SEGMENT_SEC);
        _started = false;
        return _token;
    }

    internal void Started() { if (video.PlayingChaosToken == _token && _token != 0) _started = true; }
    internal void Ended()
    {
        if (_token == 0 || video.FinishedChaosToken != _token) return;
        bool credit = _started && video.NaturalEnding;
        _token = 0;
        if (credit) endured();
    }

    // Returns true only when an on-screen chaos tape was cut.
    internal bool Tick(bool runClosing)
    {
        if (_token == 0 || (!runClosing && clock() < _cap)) return false;
        bool credit = _started && clock() >= _cap;
        bool cut = Stop();
        if (credit) endured();
        return cut;
    }

    internal bool Stop()
    {
        if (_token == 0) return false;
        int token = _token;
        bool started = _started;
        _token = 0;
        _started = false;
        video.CancelChaosVideo(token);
        return started;
    }
}
