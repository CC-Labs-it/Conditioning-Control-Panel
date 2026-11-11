using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _idleTimer;
        private bool _idleStarted;

        // WPF Speech.cs:1571: the slider sets cadence; the bark engine chooses the line.
        private void StartIdleTimer()
        {
            _idleStarted = true;
            _idleTimer ??= new DispatcherTimer();
            _idleTimer.Tick -= OnIdleTick;
            _idleTimer.Tick += OnIdleTick;
            RestartIdleTimer();
        }

        private void StopIdleTimer()
        {
            _idleStarted = false;
            _idleTimer?.Stop();
        }

        public void RestartIdleTimer() => RunOnAvatar(() =>
        {
            if (!_idleStarted || _idleTimer == null) return;
            _idleTimer.Stop();
            _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
            _idleTimer.Start();
        });

        private void ResetIdleTimer()
        {
            RestartIdleTimer();
            QuickShell?.Autonomy.ReportUserActivity();
        }

        internal void OnIdleTick(object? sender, EventArgs e)
        {
            if (!_idleStarted || _idleTimer == null) return;
            _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
            if (SpeechLatchRule.IsLatchStale(_isGiggling, _speechBubble.IsVisible, _isWaitingForAi,
                    _speechQueue.Count == 0, leadInPending: false, delayPending: _speechDelayPending))
            {
                _isGiggling = false;
                _isMouseOverSpeechBubble = false;
                _lastSpeechEndTime = SpeechNow;
            }
            if (!IsSpeechReady()) return;
            if (BarkHead.Engine is { } bark) bark.DispatchIdle();
            else Giggle(RandomBambiPhrase(), unprompted: true);
        }

        // WPF CompanionIdleVoice.ShouldVoiceNow: an idle bubble can show even in post-panic quiet.
        private static bool IdleVoiceAllowed() => !PanicAftermath.TriggersSilenced()
            && (CoreSession.IsEngineRunning || (CoreSession.IsSessionRunning && App.Sessions?.IsPaused != true)
                || CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff);
    }
}
