// WPF AvatarTubeWindow.Speech.cs:1631,2086,2147: trigger timer and panic quiet.
using System;
using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _triggerTimer;
        private Models.AppSettings? _triggerSettings;
        internal double? TriggerInterval => _triggerTimer?.Interval.TotalSeconds;
        internal Action<string> TriggerHaptic = text => { _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(text); };

        internal void StartTriggerMode()
        {
            _triggerSettings = CoreSettings.Current;
            _triggerSettings.PropertyChanged += TriggerSettingsChanged;
            RestartTriggerTimer();
        }

        internal void StopTriggerMode()
        {
            if (_triggerSettings != null) _triggerSettings.PropertyChanged -= TriggerSettingsChanged;
            _triggerSettings = null;
            _triggerTimer?.Stop();
            _triggerTimer = null;
        }

        private void TriggerSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.TriggerModeEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.TriggerIntervalSeconds))
                Dispatcher.UIThread.Post(RestartTriggerTimer);
        }

        internal void RestartTriggerTimer()
        {
            _triggerTimer?.Stop();
            _triggerTimer = null;
            if (_triggerSettings?.TriggerModeEnabled != true) return;
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_triggerSettings.TriggerIntervalSeconds) };
            _triggerTimer.Tick += (_, _) => TriggerTick();
            _triggerTimer.Start();
        }

        internal void TriggerTick()
        {
            if (_triggerSettings?.TriggerModeEnabled != true || PanicAftermath.TriggersSilenced() || !IsSpeechReady()) return;
            var triggers = _triggerSettings.CustomTriggers;
            if (triggers == null || triggers.Count == 0) return;
            ShowTrigger(triggers[_random.Next(triggers.Count)]);
        }

        internal void ShowTrigger(string text)
        {
            if (PanicAftermath.TriggersSilenced()) return;
            TriggerHaptic(text);
            if (IsMuted || !IsVisible || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted)
            {
                NoteSpeechEnded(SpeechSource.Preset, text.Length);
                return;
            }
            ShowSpeech(text, false, false, null, false, preset: true);
        }
    }
}
