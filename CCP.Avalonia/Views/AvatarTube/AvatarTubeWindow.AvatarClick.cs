// WPF AvatarTubeWindow.ChatInput.cs:48-117 and Speech.cs:839-865.
using System;
using Avalonia;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DateTime _avatarLastClick = DateTime.MinValue;
        private Action _cancelMutedIndicator = static () => { };

        internal void HandleAvatarClick()
        {
            PulseAvatarGlow();
            App.Achievements?.TrackAvatarClick();
            var now = SpeechNow;
            if ((now - _avatarLastClick).TotalMilliseconds < 300)
            {
                if (IsMuted) ShowAvatarMutedIndicator();
                else if (CoreSettings.Current.AiChatEnabled && CoreAi.IsAvailable) OpenChatInput();
            }
            _avatarLastClick = now;
        }

        // WPF ChatInput.cs:116-142: pulse the active image shadow, then restore it.
        private DispatcherTimer? _avatarGlowTimer;
        private IDisposable? _avatarGlowHold;
        private DropShadowEffect? _avatarGlow;

        private void PulseAvatarGlow()
        {
            StopAvatarGlow();
            if (PanicAftermath.TriggersSilenced()) return;
            var avatar = CurrentPortraitImage();
            if (avatar?.Effect is not DropShadowEffect original) return;
            _avatarGlow = new DropShadowEffect { Color = original.Color, BlurRadius = 20, Opacity = 0.6, OffsetX = original.OffsetX, OffsetY = original.OffsetY };
            _avatarGlowHold = avatar.SetValue(Visual.EffectProperty, _avatarGlow, BindingPriority.Animation);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timer = _avatarGlowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) => StepAvatarGlow(clock.Elapsed.TotalMilliseconds);
            timer.Start();
        }

        internal void StepAvatarGlow(double milliseconds)
        {
            if (_avatarGlow == null) return;
            if (PanicAftermath.TriggersSilenced() || milliseconds >= 400) { StopAvatarGlow(); return; }
            double amount = Math.Clamp(milliseconds <= 200 ? milliseconds / 200 : (400 - milliseconds) / 200, 0, 1);
            _avatarGlow.BlurRadius = 20 + 40 * amount;
            _avatarGlow.Opacity = 0.6 + 0.4 * amount;
        }

        private void StopAvatarGlow()
        {
            _avatarGlowTimer?.Stop();
            _avatarGlowTimer = null;
            _avatarGlowHold?.Dispose();
            _avatarGlowHold = null;
            _avatarGlow = null;
        }

        private void ShowAvatarMutedIndicator()
        {
            if (_speechBubble.IsVisible) return;
            _txtSpeech.Inlines?.Clear();
            _txtSpeech.Text = Loc.Get("avatar_muted_indicator");
            _txtSpeech.FontSize = 15;
            _speechBubble.IsVisible = true;
            _cancelMutedIndicator();
            _cancelMutedIndicator = SpeechDelayScheduler(TimeSpan.FromSeconds(2), () => _speechBubble.IsVisible = false);
        }
    }
}
