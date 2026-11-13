using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform.Deeper;

// Dispatch on the UI context: cue windows, consent and playback belong to that context.
internal sealed class SpeakActionDispatcher : IActionDispatcher, IEnhancementRunCleanup
{
    private readonly Func<TriggerEffectAction, IPlaybackTimeSource, SpeakPromptSession> _create;
    private readonly IActionDispatcher _other;
    private readonly Dictionary<string, (SpeakPromptSession Session, Task Run)> _bands = new();
    private readonly HashSet<SpeakPromptSession> _active = new();

    internal SpeakActionDispatcher(Func<TriggerEffectAction, IPlaybackTimeSource, SpeakPromptSession> create,
        IActionDispatcher? other = null) { _create = create; _other = other ?? new LoggingActionDispatcher(); }

    public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
    {
        if (action is not TriggerEffectAction { EffectType: EffectTypes.Speak } effect)
            return _other.DispatchAsync(action, ctx, ct);
        if (effect.Phase is EffectPhase.Restart or EffectPhase.Update) return Task.CompletedTask;
        lock (_bands)
        {
            var id = effect.EffectId;
            if (effect.Phase == EffectPhase.Stop)
            {
                if (id != null && _bands.TryGetValue(id, out var old)) old.Session.Stop();
                return Task.CompletedTask;
            }
            if (ct.IsCancellationRequested) return Task.CompletedTask;
            Task previous = Task.CompletedTask;
            if (effect.Phase == EffectPhase.Start && !string.IsNullOrEmpty(id) && _bands.Remove(id, out var prior))
            { prior.Session.Stop(); previous = prior.Run; }
            var session = _create(effect, ctx.Source);
            _active.Add(session);
            // Yield before completion so the dictionary owns the session even if consent is immediately denied.
            var run = RunAsync(previous, session, ct, effect.Phase == EffectPhase.Start ? id : null);
            if (effect.Phase == EffectPhase.Start && !string.IsNullOrEmpty(id)) _bands[id] = (session, run);
            return Task.CompletedTask;
        }
    }

    public void ResetOverlayBands() { }
    public void StopOneShotFlashes() { }
    public void StopOneShotSubliminals() => Views.Overlays.SubliminalWhisperShow.StopSpeak();

    internal void StopAll()
    {
        List<SpeakPromptSession> sessions;
        lock (_bands) sessions = new List<SpeakPromptSession>(_active);
        foreach (var session in sessions)
            try { session.Stop(); } catch (Exception ex) { Log.Warning(ex, "Deeper speak stop failed"); }
    }

    private async Task RunAsync(Task previous, SpeakPromptSession session, CancellationToken ct, string? id)
    {
        using var registration = ct.Register(session.Stop);
        try
        {
            await Task.Yield();
            await previous;
            if (!ct.IsCancellationRequested) await session.RunAsync();
        }
        catch (Exception ex) { Log.Warning(ex, "Deeper speak session failed"); }
        finally
        {
            lock (_bands)
                if (_active.Remove(session) && id != null && _bands.TryGetValue(id, out var current) && ReferenceEquals(current.Session, session))
                    _bands.Remove(id);
        }
    }
}
