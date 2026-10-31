using System;
using System.Collections.Generic;
using System.Threading;
namespace ConditioningControlPanel.Services;
/// <summary>WPF ConditioningControlPanel/Services/MantraChantService.cs:45-50,62-99,231-287,358-368:
/// pair/gap/mute rules, stepped by the head's clock.
/// No auto-start: only an explicit user toggle calls Start.</summary>
public sealed class MantraChantLoop(
    Func<bool> canChant, Func<IReadOnlyList<string>> pick,
    Func<bool> muted, Func<int> gapSeconds,
    Func<string, long, bool> play, Action stopAudio)
{
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private bool _playing, _parked, _running;
    private long _stopEpoch, _runEpoch;
    private TimeSpan? _due;
    private long _generation;
    public bool IsRunning => _running && Volatile.Read(ref _stopEpoch) == _runEpoch;
    public TimeSpan? NextDue { get { lock (_gate) return _due; } }
    public void Start(TimeSpan now)
    {
        lock (_gate)
        {
            var epoch = Volatile.Read(ref _stopEpoch);
            if (_running && !IsRunning) Stop();
            if (IsRunning || !canChant()) return;
            _runEpoch = epoch;
            _running = true;
            Next(now);
        }
    }
    public void StopEmergency() { Interlocked.Increment(ref _stopEpoch); stopAudio(); } // WPF MantraChantService.cs:125-135, no playback-I/O lock.
    public void Stop()
    {
        lock (_gate)
        {
            _running = _playing = _parked = false;
            _due = null;
            _pending.Clear();
            ++_generation; // a late audio completion can never revive a stopped run
            stopAudio();
        }
    }
    public void Tick(TimeSpan now)
    {
        lock (_gate)
        {
            if (!IsRunning) return;
            if (muted())
            {
                if (!_parked)
                {
                    _parked = true;
                    _playing = false;
                    _due = null;
                    _pending.Clear();
                    ++_generation;
                    stopAudio();
                }
                return;
            }
            if (_parked) { _parked = false; Next(now); }
            else if (!_playing && _due is { } due && now >= due) Next(now);
        }
    }
    public void ClipEnded(long generation, TimeSpan now)
    {
        lock (_gate)
        {
            if (!IsRunning || !_playing || generation != _generation) return;
            _playing = false;
            Wait(now);
        }
    }
    private void Wait(TimeSpan now) => _due = now + (_pending.Count > 0
        ? TimeSpan.FromSeconds(2.5)
        : TimeSpan.FromSeconds(Math.Clamp(gapSeconds(), 0, 60)));
    private void Next(TimeSpan now)
    {
        _due = null;
        if (!IsRunning) return;
        if (muted()) { _pending.Clear(); _parked = true; return; }
        for (var i = 0; _pending.Count == 0 && i < 12; ++i)
            foreach (var path in pick()) if (!string.IsNullOrEmpty(path)) _pending.Enqueue(path);
        if (_pending.Count == 0) { Stop(); return; }
        var clip = _pending.Dequeue();
        _playing = true;
        var generation = ++_generation;
        try { var started = IsRunning && play(clip, generation); if (!IsRunning) { stopAudio(); return; } if (started) return; }
        catch { /* WPF: a bad clip moves on after the next wait. */ }
        if (!IsRunning) { stopAudio(); return; }
        _playing = false;
        Wait(now);
    }
    public static double ResolveVolume(bool muted, double chant, double master) => muted
        ? 0 : Math.Clamp(chant / 100 * master / 100, 0, 1);
    public static IReadOnlyList<string> ResolveClips(string? prompt, string? response)
    {
        var clips = new List<string>(2);
        if (!string.IsNullOrEmpty(prompt)) clips.Add(prompt);
        if (!string.IsNullOrEmpty(response)) clips.Add(response);
        return clips;
    }
}
