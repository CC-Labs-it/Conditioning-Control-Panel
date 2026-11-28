using System;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>What the camera check asks of the webcam. Tests hand in a fake.</summary>
public interface IDareCameraProbe
{
    /// <summary>The camera is open (tracking, or open with no face found).</summary>
    bool Running { get; }

    /// <summary>A face is in view right now.</summary>
    bool FaceSeen { get; }

    /// <summary>The player stopped the camera by hand (<c>AppSettings.CameraHeldOff</c>): it is never opened for them.</summary>
    bool HeldOff { get; }

    /// <summary>Open the camera. False when it cannot be (no consent, no device).</summary>
    bool TryStart();

    void Stop();
}

/// <summary>
/// THE CAMERA CHECK (CONTRACT.md "Counting", camera). While a camera bet's counting is live it looks
/// every <see cref="DareGrammar.CameraSampleMs"/> and adds the step to "counted", and to "seen" when a
/// face is there; pass = seen at least <see cref="DareGrammar.CameraPassFraction"/> of counted (the
/// server judges). The presence answer is the webcam service's own face tracking; no frame is kept,
/// stored or sent, only the two sums leave, inside <c>report</c>.
///
/// <para>It opens the camera only when the player has not stopped it by hand, opens it at most once
/// per live stretch, and closes it again only if it was the one that opened it. Everything goes
/// through <see cref="Step"/>, so the rules are tested without a timer.</para>
/// </summary>
public sealed class DareCameraCheck
{
    private readonly IDareCameraProbe _probe;
    private bool _openedByUs;
    private bool _triedThisStretch;
    private DateTimeOffset? _last;

    public DareCameraCheck(IDareCameraProbe probe) { _probe = probe; }

    public DareCameraState State { get; private set; } = DareCameraState.Idle;

    /// <summary>One look. Returns true while the check wants to keep looking.</summary>
    public bool Step(DareMeter meter, DareLive live, DateTimeOffset now)
    {
        if (!meter.CameraWanted(now, live))
        {
            _last = null;
            _triedThisStretch = false;
            if (_openedByUs)
            {
                _openedByUs = false;
                try { _probe.Stop(); } catch (Exception ex) { Serilog.Log.Debug("Dares camera stop failed: {E}", ex.Message); }
            }
            State = DareCameraState.Idle;
            return false;
        }

        bool running;
        try { running = _probe.Running; } catch { running = false; }
        if (!running)
        {
            bool held;
            try { held = _probe.HeldOff; } catch { held = true; }
            if (held) State = DareCameraState.HeldOff;
            else if (!_triedThisStretch)
            {
                _triedThisStretch = true;
                bool ok;
                try { ok = _probe.TryStart(); } catch { ok = false; }
                if (ok) _openedByUs = true;
                State = ok ? DareCameraState.Watching : DareCameraState.Unavailable;
            }
        }
        else State = DareCameraState.Watching;

        bool seen;
        try { seen = running && _probe.FaceSeen; } catch { seen = false; }
        var step = _last is { } l ? (now - l).TotalSeconds : DareGrammar.CameraSampleMs / 1000.0;
        _last = now;
        // A camera that is off counts the time and sees nothing: the honest answer for a camera bet.
        meter.CameraSample(step, seen, live, now);
        return true;
    }
}
