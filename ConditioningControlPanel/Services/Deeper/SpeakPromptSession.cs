using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Drives one <c>speak</c> Deeper effect: shows an on-screen cue ("Say YES"), opens the
    /// offline recognizer, scores what the user says against the target phrase, flashes
    /// correct/incorrect feedback, and counts successful reps. When the band is set to
    /// <see cref="SpeakCompletion.UntilSatisfied"/> it holds the region (loop / pause / keep
    /// playing) until the required reps are met. Modeled on
    /// <c>LockCardWindow.RunVoiceSolveLoopAsync</c>; fully best-effort and never throws out
    /// of the engine tick. One session per band <c>EffectId</c>, owned by the dispatcher.
    /// </summary>
    public sealed class SpeakPromptSession
    {
        // Asked for mic consent at most once per app run, even across many prompts.
        private static bool _promptedConsentThisRun;

        private readonly SpeakPromptPlan _plan;
        private readonly IPlaybackTimeSource? _source;

        private CancellationTokenSource? _cts;
        private DispatcherTimer? _cueTimer;
        private DispatcherTimer? _holdTimer;
        private int _reps;
        private bool _paused;       // we issued source.Pause() and must resume on completion
        private bool _evictedAutonomy; // we stood the "Hey Bambi" wake/PTT mic down and must restore it
        private volatile bool _completed;
        private volatile bool _stopped;

        public SpeakPromptSession(TriggerEffectAction effect, IPlaybackTimeSource? source)
        {
            _plan = new SpeakPromptPlan(effect, DateTime.UtcNow);
            _source = source;
        }

        /// <summary>Begin the prompt. Returns immediately; the cue + listen loop run async.</summary>
        public void Start()
        {
            // Decouple from the engine tick: consent is modal and the cue timer must live on
            // the UI thread, so always marshal onto the dispatcher.
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(new Action(StartCore));
        }

        private void StartCore()
        {
            try
            {
                if (_stopped) return;
                if (string.IsNullOrWhiteSpace(_plan.Target))
                {
                    App.Logger?.Debug("SpeakPromptSession: empty target — skipped");
                    return;
                }
                if (App.Speech?.IsAvailable != true)
                {
                    App.Logger?.Information("SpeakPromptSession: speech unavailable (no model/mic) — skipping prompt");
                    return;
                }

                // Mic consent: prompt once per run. Declined => skip silently forever after.
                if (App.Settings?.Current?.MicConsentGiven != true)
                {
                    if (_promptedConsentThisRun) return;
                    _promptedConsentThisRun = true;
                    try
                    {
                        var dlg = new MicConsentDialog { Owner = Application.Current?.MainWindow };
                        var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                        if (!ok || App.Settings?.Current?.MicConsentGiven != true)
                        {
                            App.Logger?.Information("SpeakPromptSession: mic consent declined — skipping prompt");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("SpeakPromptSession: consent dialog failed: {E}", ex.Message);
                        return;
                    }
                }

                if (_stopped) return;

                // The mic is single-owner. If the always-on "Hey Bambi" wake-word loop (or
                // push-to-talk) is armed it holds the mic forever, so our RecognizePhraseAsync
                // calls get rejected as "session already active". Stand it down for the duration
                // of this prompt; RestoreAutonomy() re-arms it per settings when we finish.
                try
                {
                    if (App.Autonomy?.UserDrivenVoiceArmed == true)
                    {
                        App.Autonomy.StopVoiceInput();
                        _evictedAutonomy = true;
                        App.Logger?.Information("SpeakPromptSession: claimed mic from Autonomy wake/PTT for the prompt");
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("SpeakPromptSession: evict Autonomy failed: {E}", ex.Message); }

                _cts = new CancellationTokenSource();
                App.Logger?.Information("SpeakPromptSession: prompt started — target='{Target}' reps={Reps} cue={Cue} completion={Comp} hold={Hold}",
                    _plan.Target, _plan.RequiredReps, _plan.CueMode, _plan.Completion, _plan.HoldMode);
                StartCue();
                StartHoldWatch();
                _ = RunLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("SpeakPromptSession.StartCore: {E}", ex.Message);
            }
        }

        // ---- cue presentation ---------------------------------------------------

        private void StartCue()
        {
            if (_plan.CueMode == SpeakCueMode.Persistent)
            {
                SpeakCueOverlay.ShowCue(_plan.Cue);
                return;
            }
            // Intermittent: flash once now, then on a timer.
            FlashCue();
            _cueTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(_plan.CueIntervalMs)
            };
            _cueTimer.Tick += (_, _) => FlashCue();
            _cueTimer.Start();
        }

        private void FlashCue()
        {
            try { App.Subliminal?.FlashSubliminalCustom(_plan.Cue, suppressHaptic: true); }
            catch (Exception ex) { App.Logger?.Debug("SpeakPromptSession.FlashCue: {E}", ex.Message); }
        }

        private void StopCue()
        {
            try { _cueTimer?.Stop(); } catch { }
            _cueTimer = null;
            if (_plan.CueMode == SpeakCueMode.Persistent)
                SpeakCueOverlay.HideCue();
        }

        private void FlashFeedback(string text)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(new Action(() =>
            {
                try { App.Subliminal?.FlashSubliminalCustom(text, suppressHaptic: true); }
                catch (Exception ex) { App.Logger?.Debug("SpeakPromptSession.FlashFeedback: {E}", ex.Message); }
            }));
        }

        // ---- hold (loop / pause near region end) --------------------------------

        private void StartHoldWatch()
        {
            if (!_plan.HoldsPlayback) return;
            if (_source == null) return;

            // #1221: no region end to police on a rule-fired speak - SpeakPromptPlan.PausesAtCue.
            if (!_plan.HasRegion)
            {
                if (_plan.PausesAtCue && !_paused)
                {
                    try
                    {
                        _source.Pause();
                        _paused = true;
                    }
                    catch (Exception ex) { App.Logger?.Debug("SpeakPromptSession.StartHoldWatch: {E}", ex.Message); }
                }
                return;
            }

            _holdTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _holdTimer.Tick += (_, _) => HoldTick();
            _holdTimer.Start();
        }

        private void HoldTick()
        {
            try
            {
                if (_completed || _stopped) return;
                if (_source == null) return;

                switch (_plan.HoldStepAt(_source.GetCurrentTimeSeconds(), _paused))
                {
                    case SpeakHoldStep.SeekToRegionStart:
                        _source.Seek(_plan.RegionStart!.Value);
                        break;
                    case SpeakHoldStep.Pause:
                        _source.Pause();
                        _paused = true;
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("SpeakPromptSession.HoldTick: {E}", ex.Message); }
        }

        private void ReleaseHold()
        {
            try { _holdTimer?.Stop(); } catch { }
            _holdTimer = null;
            if (_paused)
            {
                _paused = false;
                try { _source?.Play(); } catch (Exception ex) { App.Logger?.Debug("SpeakPromptSession.ReleaseHold: {E}", ex.Message); }
            }
        }

        // Hand the mic back to the "Hey Bambi" wake/PTT loop if we stood it down. Idempotent.
        private void RestoreAutonomy()
        {
            if (!_evictedAutonomy) return;
            _evictedAutonomy = false;
            try { App.Autonomy?.RefreshVoiceInputModes(); } catch (Exception ex) { App.Logger?.Debug("SpeakPromptSession.RestoreAutonomy: {E}", ex.Message); }
        }

        // ---- listen loop --------------------------------------------------------

        private async Task RunLoopAsync(CancellationToken ct)
        {
            int consecutiveUnavailable = 0;
            try
            {
                // If we just stood Autonomy down, give its capture session a beat to release
                // the mic before our first listen (mirrors AutonomyService.RequestVoiceCommand).
                if (_evictedAutonomy)
                {
                    for (int i = 0; i < 24 && App.Speech?.IsListening == true && !ct.IsCancellationRequested; i++)
                        await Task.Delay(25, ct).ConfigureAwait(false);
                }

                while (!ct.IsCancellationRequested && _reps < _plan.RequiredReps)
                {
                    if (_plan.HardDeadline.HasValue && DateTime.UtcNow >= _plan.HardDeadline.Value) break;

                    if (App.Speech?.IsAvailable != true)
                    {
                        if (++consecutiveUnavailable > 8) break;
                        await Task.Delay(400, ct).ConfigureAwait(false);
                        continue;
                    }

                    PhraseResult res;
                    try
                    {
                        res = await App.Speech.RecognizePhraseAsync(
                            _plan.Target, new RecognizeOptions { Timeout = TimeSpan.FromSeconds(10) }, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { res = PhraseResult.NotAvailable; }

                    if (ct.IsCancellationRequested) break;

                    App.Logger?.Debug("SpeakPromptSession: heard {Chars} chars matched={M} score={S:0.00} loud={L} timedOut={T} unavail={U}",
                        (res.Transcript ?? "").Length, res.Matched, res.Score, res.LoudEnough, res.TimedOut, res.Unavailable);

                    var heard = SpeakPromptPlan.Classify(res);
                    if (heard == SpeakHeard.Retry)
                    {
                        // Mic briefly held by another session — retry (rare now we evict Autonomy).
                        await Task.Delay(200, ct).ConfigureAwait(false);
                        continue;
                    }
                    consecutiveUnavailable = 0;

                    if (heard == SpeakHeard.Correct)
                    {
                        _reps++;
                        FlashFeedback(_plan.CorrectMessage);
                        Giggle(_plan.CorrectMessage);
                        App.Logger?.Information("SpeakPromptSession: correct ({Reps}/{Need}, {Chars} chars heard)", _reps, _plan.RequiredReps, (res.Transcript ?? "").Length);
                        if (_reps >= _plan.RequiredReps) break;
                        await Task.Delay(600, ct).ConfigureAwait(false);
                    }
                    else if (heard == SpeakHeard.Incorrect)
                    {
                        FlashFeedback(_plan.IncorrectMessage);
                        await Task.Delay(700, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { /* stopped */ }
            catch (Exception ex) { App.Logger?.Warning("SpeakPromptSession loop: {E}", ex.Message); }
            finally
            {
                // Reps met (or deadline hit): clear the cue + release any hold so playback resumes.
                MarshalComplete();
            }
        }

        private void Giggle(string text)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(new Action(() =>
            {
                try { App.AvatarWindow?.GigglePriority(text, playSound: false, aiGenerated: false); } catch { }
            }));
        }

        private void MarshalComplete()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(new Action(() =>
            {
                if (_stopped) return;
                _completed = true;
                StopCue();
                ReleaseHold();
                RestoreAutonomy();
            }));
        }

        /// <summary>Tear the prompt down (band exit / engine stop). Safe to call repeatedly.</summary>
        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            try { _cts?.Cancel(); } catch { }

            var disp = Application.Current?.Dispatcher;
            void Teardown()
            {
                StopCue();
                ReleaseHold();
                try { if (App.Speech?.IsListening == true) App.Speech.StopListening(); } catch { }
                RestoreAutonomy();
            }
            if (disp == null || disp.CheckAccess()) Teardown();
            else disp.BeginInvoke(new Action(Teardown));
        }
    }
}
