using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Avalonia.Platform.Deeper;

// The caller runs this on its UI context. The adapters keep capture and windows in the head.
internal sealed class SpeakPromptSession
{
    private readonly SpeakPromptPlan _plan;
    private readonly IPlaybackTimeSource? _source;
    private readonly Func<bool> _available;
    private readonly Func<Task<bool>> _consent;
    private readonly Func<bool> _borrowMic;
    private readonly Action _restoreMic;
    private readonly Func<string, CancellationToken, Task<PhraseResult>> _listen;
    private readonly Action<string> _show;
    private readonly Action _hide;
    private readonly Action<string, bool> _feedback;
    private readonly CancellationTokenSource _stop = new();
    private bool _started;

    internal SpeakPromptSession(TriggerEffectAction effect, IPlaybackTimeSource? source,
        Func<bool> available, Func<Task<bool>> consent, Func<bool> borrowMic, Action restoreMic,
        Func<string, CancellationToken, Task<PhraseResult>> listen, Action<string> show, Action hide,
        Action<string, bool>? feedback = null)
    {
        _plan = new SpeakPromptPlan(effect, DateTime.UtcNow);
        _source = source;
        _available = available;
        _consent = consent;
        _borrowMic = borrowMic;
        _restoreMic = restoreMic;
        _listen = listen;
        _show = show;
        _hide = hide;
        _feedback = feedback ?? Feedback;
    }

    private static void Feedback(string text, bool correct)
    {
        try
        {
            var sanitized = text.Trim();
            if (sanitized.Length > 200) sanitized = sanitized.Substring(0, 200);
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "<[^>]*>", "");
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                Views.Overlays.SubliminalWhisperShow.Draw(sanitized);
                CoreProgression.AddXP(10, "Subliminal");
            }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Speak feedback flash failed"); }
        try { if (correct) Views.AvatarTube.AvatarTubeWindow.Live?.GigglePriority(text, playSound: false, aiGenerated: false); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Speak feedback avatar failed"); }
    }

    // Stop also works while the consent dialog is open. No side effects follow a stopped dialog.
    internal void Stop() => _stop.Cancel();

    internal async Task RunAsync()
    {
        if (_started) throw new InvalidOperationException("Session already started");
        _started = true;
        var ct = _stop.Token;
        bool borrowed = false, shown = false, paused = false;
        using var holdStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? hold = null, cue = null;
        try
        {
            if (ct.IsCancellationRequested ||
                string.IsNullOrWhiteSpace(_plan.Target) || !_available()) return;
            if (!await _consent() || ct.IsCancellationRequested || !_available()) return;
            borrowed = _borrowMic();
            if (ct.IsCancellationRequested) return;
            if (_plan.CueMode == SpeakCueMode.Persistent)
            {
                shown = true;
                _show(_plan.Cue);
            }
            else
            {
                FlashCue();
                cue = CueAsync(holdStop.Token);
            }
            if (_plan.PausesAtCue && _source != null)
            {
                _source.Pause();
                paused = true;
            }
            if (_plan.HasRegion && _plan.HoldsPlayback && _source != null)
                hold = HoldAsync(holdStop.Token);
            int reps = 0, unavailable = 0;
            while (!ct.IsCancellationRequested && reps < _plan.RequiredReps &&
                   (!_plan.HardDeadline.HasValue || DateTime.UtcNow < _plan.HardDeadline.Value))
            {
                if (!_available())
                {
                    if (++unavailable > 8) return;
                    await Task.Delay(400, ct);
                    continue;
                }
                PhraseResult result;
                try { result = await _listen(_plan.Target, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { result = PhraseResult.NotAvailable; }
                ct.ThrowIfCancellationRequested();
                switch (SpeakPromptPlan.Classify(result))
                {
                    case SpeakHeard.Correct:
                        reps++; unavailable = 0;
                        _feedback(_plan.CorrectMessage, true);
                        if (reps < _plan.RequiredReps) await Task.Delay(600, ct);
                        break;
                    case SpeakHeard.Incorrect:
                        unavailable = 0;
                        _feedback(_plan.IncorrectMessage, false);
                        await Task.Delay(700, ct);
                        break;
                    case SpeakHeard.Retry:
                        await Task.Delay(200, ct);
                        break;
                    default: unavailable = 0; break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            holdStop.Cancel();
            try
            {
                if (hold != null) try { await hold; } catch (OperationCanceledException) { }
                if (cue != null) try { await cue; } catch (OperationCanceledException) { }
            }
            finally
            {
                try { if (shown) _hide(); }
                finally
                {
                    try { if (paused) _source?.Play(); }
                    finally { if (borrowed) _restoreMic(); }
                }
            }
        }

        void FlashCue()
        {
            try { CoreSubliminal.CustomShowProvider?.Invoke(_plan.Cue, true); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Speak cue flash failed"); }
        }

        async Task CueAsync(CancellationToken token)
        {
            while (true)
            {
                await Task.Delay(_plan.CueIntervalMs, token);
                FlashCue();
            }
        }

        async Task HoldAsync(CancellationToken token)
        {
            while (true)
            {
                await Task.Delay(120, token);
                switch (_plan.HoldStepAt(_source!.GetCurrentTimeSeconds(), paused))
                {
                    case SpeakHoldStep.Pause: _source.Pause(); paused = true; break;
                    case SpeakHoldStep.SeekToRegionStart: _source.Seek(_plan.RegionStart!.Value); break;
                }
            }
        }
    }
}
