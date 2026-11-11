// WPF Controls/Leash/LeashSurfaces.cs:127-170. Ended belongs to the cut surface.
using System;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed class LeashEventCues : IDisposable
{
    private readonly Action<LeashCue> _play;
    private ILeashService? _service;
    internal LeashEventCues(Action<LeashCue> play)
    {
        _play = play;
        LeashHead.ServiceChanged += Rewire;
        Rewire();
    }
    private void Rewire()
    {
        if (_service != null) _service.EventArrived -= OnEvent;
        _service = LeashHead.Service;
        if (_service != null) _service.EventArrived += OnEvent;
    }
    internal void OnEvent(LeashEvent e)
    {
        LeashCue? cue = e.Kind switch
        {
            LeashEventKind.Tug => LeashCue.Jingle,
            LeashEventKind.Answered when e.Accepted != true => LeashCue.Refused,
            LeashEventKind.PunishSkipped => LeashCue.Refused,
            LeashEventKind.Reward => LeashCue.Gift,
            LeashEventKind.Punish or LeashEventKind.AssignMissed => LeashCue.Scold,
            LeashEventKind.Assign => LeashCue.Assigned,
            LeashEventKind.AssignDone or LeashEventKind.PunishDone => LeashCue.Done,
            _ => null,
        };
        if (cue is { } c) _play(c);
    }
    public void Dispose()
    {
        LeashHead.ServiceChanged -= Rewire;
        if (_service != null) _service.EventArrived -= OnEvent;
        _service = null;
    }
}
