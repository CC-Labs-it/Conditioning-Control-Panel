using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Avalonia.Platform.Deeper;

// One instance per app run: a declined consent prompt is not repeated.
internal sealed class SpeakPromptRuntime
{
    private readonly Func<bool> _available, _consented, _armed, _listening;
    private readonly Func<Task<bool>> _askConsent;
    private readonly Action _stopVoice, _restoreVoice, _stopListening, _hide;
    private readonly Func<string, RecognizeOptions, CancellationToken, Task<PhraseResult>> _recognize;
    private readonly Action<string> _show, _flash;
    private Task<bool>? _consentAttempt;

    internal SpeakPromptRuntime(Func<bool> available, Func<bool> consented, Func<Task<bool>> askConsent,
        Func<bool> armed, Action stopVoice, Action restoreVoice, Func<bool> listening,
        Func<string, RecognizeOptions, CancellationToken, Task<PhraseResult>> recognize, Action stopListening,
        Action<string> show, Action hide, Action<string>? flash = null)
    {
        _available = available; _consented = consented; _askConsent = askConsent;
        _armed = armed; _stopVoice = stopVoice; _restoreVoice = restoreVoice; _listening = listening;
        _recognize = recognize; _stopListening = stopListening; _show = show; _hide = hide;
        _flash = flash ?? SpeakPromptSession.FlashCustom;
    }

    internal SpeakPromptSession CreateSession(TriggerEffectAction effect, IPlaybackTimeSource? source)
    {
        bool evicted = false, waited = false;
        int capturing = 0, cueStopped = 0;
        global::Avalonia.Threading.DispatcherTimer? timer = null;
        bool intermittent = effect.SpeakCueMode == SpeakCueMode.Intermittent;
        return new SpeakPromptSession(effect, source, _available, ConsentAsync,
            () => { if (!_armed()) return false; _stopVoice(); evicted = true; return true; },
            _restoreVoice,
            async (target, ct) =>
            {
                Interlocked.Exchange(ref capturing, 1);
                try
                {
                    if (evicted && !waited) { waited = true; for (int i = 0; i < 24 && _listening(); i++) await Task.Delay(25, ct); }
                    ct.ThrowIfCancellationRequested();
                    return await _recognize(target, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(10) }, ct);
                }
                finally { Interlocked.Exchange(ref capturing, 0); }
            }, cue =>
            {
                if (!intermittent) { _show(cue); return; }
                _flash(cue);
                timer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(80, effect.SpeakCueIntervalMs)) };
                timer.Tick += (_, _) => { if (Volatile.Read(ref cueStopped) == 0) _flash(cue); };
                timer.Start();
            }, () => { if (intermittent) timer?.Stop(); else _hide(); }, stopCapture: () =>
            {
                Interlocked.Exchange(ref cueStopped, 1);
                timer?.Stop();
                if (Volatile.Read(ref capturing) != 0) _stopListening();
            }, intermittentCues: true);
    }

    private async Task<bool> ConsentAsync()
    {
        if (_consented()) return true;
        var attempt = _consentAttempt ??= AskOnceAsync();
        return await attempt && _consented();
    }

    private async Task<bool> AskOnceAsync()
    {
        try { return await _askConsent(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Speak consent failed"); return false; }
    }

    internal static SpeakPromptRuntime CreateProduction(Func<Views.Windows.MainShellWindow?> shell) => new(
        () => CoreSpeech.IsAvailable,
        () => CoreSettings.Current.MicConsentGiven,
        async () =>
        {
            var owner = shell();
            if (owner == null) return false;
            var dialog = new Views.Dialogs.MicConsentDialog();
            await dialog.ShowDialogSafe(owner);
            return dialog.ConsentGiven && CoreSettings.Current.MicConsentGiven;
        },
        () => shell() is { } s && (s.WakeLoopArmed || s.PushToTalkArmed),
        () => shell()?.StopVoiceInput(),
        () => shell()?.RefreshVoiceInputModes(),
        () => PulseMicSource.Speech?.IsListening == true,
        (target, options, ct) => PulseMicSource.Speech?.RecognizePhraseAsync(target, options, ct) ?? Task.FromResult(PhraseResult.NotAvailable),
        () => PulseMicSource.Speech?.StopListening(),
        Views.Overlays.SpeakCueOverlay.ShowCue,
        Views.Overlays.SpeakCueOverlay.HideCue);
}
