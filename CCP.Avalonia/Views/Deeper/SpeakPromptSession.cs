using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>WPF SpeakPromptSession.cs:53-118,154-163,227-349. Owned by the future action dispatcher.</summary>
internal sealed class SpeakPromptSession
{
    internal static bool PromptedConsentThisRun;
    private readonly SpeakPromptPlan _plan;
    private readonly SpeakPromptCueSession _cue;
    private readonly CancellationTokenSource _stop = new();
    private bool _started, _stopped, _evicted, _recognizing;

    // Instance seams let tests exercise the same session without a microphone or modal dialog.
    internal Func<bool> Available = () => PulseMicSource.Speech?.IsAvailable == true;
    internal Func<bool> Consent = () => CoreSettings.Current.MicConsentGiven;
    internal Func<Task<bool>> AskConsent = async () =>
    {
        var dialog = new MicConsentDialog();
        return await dialog.ShowDialogSafe<bool>(MainShellWindow.Current) && dialog.ConsentGiven;
    };
    internal Func<bool> EvictVoice = () =>
    {
        if (MainShellWindow.Current is not { } shell || !(shell.WakeLoopArmed || shell.PushToTalkArmed)) return false;
        shell.StopVoiceInput();
        return true;
    };
    internal Action RestoreVoice = () => MainShellWindow.Current?.RefreshVoiceInputModes();
    internal Func<bool> IsListening = () => PulseMicSource.Speech?.IsListening == true;
    internal Action StopListening = () => PulseMicSource.Speech?.StopListening();
    internal Func<string, RecognizeOptions, CancellationToken, Task<PhraseResult>> Recognize = (target, options, ct) =>
        PulseMicSource.Speech?.RecognizePhraseAsync(target, options, ct) ?? Task.FromResult(PhraseResult.NotAvailable);
    internal Func<int, CancellationToken, Task> Delay = (ms, ct) => Task.Delay(ms, ct);
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal Action<string> Flash = FlashCustom;
    internal Action<string> Giggle = text => MainShellWindow.Current?.Tube?.GigglePriority(text, playSound: false, aiGenerated: false);

    internal SpeakPromptSession(TriggerEffectAction effect, IPlaybackTimeSource? source)
    {
        _plan = new SpeakPromptPlan(effect, DateTime.UtcNow);
        _cue = new SpeakPromptCueSession(_plan, text => Flash(text), source: source);
    }

    private static void FlashCustom(string text)
    {
        // WPF SubliminalService.cs:311-323,576-599: custom text is sanitized, immediate and has no haptic.
        if (string.IsNullOrWhiteSpace(text)) return;
        text = SanitizeFlash(text);
        if (MainShellWindow.Current is { } host) SubliminalOverlay.Show(host, text);
        CoreProgression.AddXP(10, "Subliminal");
    }

    internal static string SanitizeFlash(string text)
    {
        text = text.Trim();
        if (text.Length > 200) text = text.Substring(0, 200);
        return Regex.Replace(text, "<[^>]*>", "");
    }

    internal void Start() => Dispatcher.UIThread.Post(() => _ = StartAsync());

    // UI-thread owned, including the asynchronous consent continuation and final cleanup.
    internal async Task StartAsync()
    {
        if (_started || _stopped) return;
        _started = true;
        try
        {
            if (string.IsNullOrWhiteSpace(_plan.Target) || !Available()) return;
            if (!Consent())
            {
                if (PromptedConsentThisRun) return;
                PromptedConsentThisRun = true;
                if (!await AskConsent() || !Consent()) return;
            }
            if (_stopped) return;
            try { _evicted = EvictVoice(); }
            catch (Exception ex) { Log.Debug("Speak prompt mic handoff: {E}", ex.Message); }
            _cue.Start();
            await ListenAsync(_stop.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warning("Speak prompt: {E}", ex.Message); }
        finally { Finish(); }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        int reps = 0, unavailable = 0;
        if (_evicted)
            for (int i = 0; i < 24 && IsListening() && !ct.IsCancellationRequested; i++) await Delay(25, ct);
        while (!ct.IsCancellationRequested && reps < _plan.RequiredReps)
        {
            if (_plan.HardDeadline is { } deadline && UtcNow() >= deadline) break;
            if (!Available())
            {
                if (++unavailable > 8) break;
                await Delay(400, ct);
                continue;
            }
            PhraseResult result;
            try
            {
                _recognizing = true;
                result = await Recognize(_plan.Target, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(10) }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { result = PhraseResult.NotAvailable; }
            finally { _recognizing = false; }
            if (ct.IsCancellationRequested) break;
            var heard = SpeakPromptPlan.Classify(result);
            if (heard == SpeakHeard.Retry) { await Delay(200, ct); continue; }
            unavailable = 0;
            if (heard == SpeakHeard.Correct)
            {
                reps++;
                Feedback(_plan.CorrectMessage);
                try { Giggle(_plan.CorrectMessage); } catch { }
                if (reps < _plan.RequiredReps) await Delay(600, ct);
            }
            else if (heard == SpeakHeard.Incorrect)
            {
                Feedback(_plan.IncorrectMessage);
                await Delay(700, ct);
            }
        }
    }

    private void Feedback(string text)
    {
        try { Flash(text); } catch (Exception ex) { Log.Debug("Speak prompt feedback: {E}", ex.Message); }
    }

    internal void Stop()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Stop); return; }
        if (_stopped) return;
        _stopped = true;
        try { _stop.Cancel(); } catch { }
        _cue.Dispose();
        try { if (_recognizing && IsListening()) StopListening(); } catch { }
        Restore();
    }

    private void Finish()
    {
        _cue.Dispose();
        Restore();
    }

    private void Restore()
    {
        if (!_evicted) return;
        _evicted = false;
        try { RestoreVoice(); } catch (Exception ex) { Log.Debug("Speak prompt restore voice: {E}", ex.Message); }
    }
}
