// WPF AvatarTubeWindow.ChatInput.cs:48-117 and Speech.cs:839-865.
using System;
using Avalonia;
using Avalonia.Data;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DateTime _avatarLastClick = DateTime.MinValue;
        private Action _cancelMutedIndicator = static () => { };

        internal void HandleAvatarClick()
        {
            PulseAvatarGlow();
            PlayClickBounce();
            App.Achievements?.TrackAvatarClick();
            var now = SpeechNow;
            if ((now - _avatarLastClick).TotalMilliseconds < 300)
            {
                if (IsMuted) ShowAvatarMutedIndicator();
                else if (CoreSettings.Current.AiChatEnabled && CoreAi.IsAvailable) OpenChatInput();
            }
            _avatarLastClick = now;
        }

        // WPF ChatInput.cs:154-177. A separate host composes with the chat reply bounce.
        private BeatLoop? _clickBounceLoop;
        private ScaleTransform? _clickBounceScale;

        private void PlayClickBounce()
        {
            StopClickBounce();
            var host = this.FindControl<global::Avalonia.Controls.Grid>("ClickBounceHost");
            if (host == null || ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()) return;
            _clickBounceScale = new ScaleTransform(1, 1);
            host.RenderTransform = _clickBounceScale;
            _clickBounceLoop = new BeatLoop(host, seconds => StepClickBounce(seconds * 1000)) { OnParked = StopClickBounce };
            _clickBounceLoop.Start();
        }

        internal void StepClickBounce(double ms)
        {
            if (_clickBounceScale == null) return;
            if (ms >= 300) { StopClickBounce(); return; }
            double t = Math.Clamp(ms / 80, 0, 1);
            double value;
            if (ms <= 80) value = 1 + 0.015 * (1 - Math.Pow(1 - t, 3));
            else
            {
                t = (ms - 80) / 220;
                // WPF ElasticEase, one oscillation, springiness 7, ease out.
                double u = 1 - t;
                double easeIn = (Math.Exp(7 * u) - 1) / (Math.Exp(7) - 1) * Math.Sin((2 * Math.PI + Math.PI / 2) * u);
                value = 1 + 0.015 * easeIn;
            }
            _clickBounceScale.ScaleX = _clickBounceScale.ScaleY = value;
        }

        private void OnClickBounceEngineStopped(object? sender, EventArgs e) => RunOnAvatar(StopClickBounce);

        private void StopClickBounce()
        {
            _clickBounceLoop?.Stop();
            _clickBounceLoop = null;
            var host = this.FindControl<global::Avalonia.Controls.Grid>("ClickBounceHost");
            if (host != null) host.RenderTransform = null;
            _clickBounceScale = null;
        }

        // WPF ChatInput.cs:116-142: pulse the active image shadow, then restore it.
        private BeatLoop? _avatarGlowLoop;
        private IDisposable? _avatarGlowHold;
        private DropShadowEffect? _avatarGlow;

        private void PulseAvatarGlow()
        {
            StopAvatarGlow();
            var avatar = CurrentPortraitImage();
            if (avatar?.Effect is not DropShadowEffect original) return;
            _avatarGlow = new DropShadowEffect { Color = original.Color, BlurRadius = 20, Opacity = 0.6, OffsetX = original.OffsetX, OffsetY = original.OffsetY };
            _avatarGlowHold = avatar.SetValue(Visual.EffectProperty, _avatarGlow, BindingPriority.Animation);
            _avatarGlowLoop = new BeatLoop(avatar, seconds => StepAvatarGlow(seconds * 1000))
            {
                OnParked = StopAvatarGlow
            };
            _avatarGlowLoop.Start();
        }

        internal void StepAvatarGlow(double milliseconds)
        {
            if (_avatarGlow == null) return;
            if (milliseconds >= 400) { StopAvatarGlow(); return; }
            double amount = Math.Clamp(milliseconds <= 200 ? milliseconds / 200 : (400 - milliseconds) / 200, 0, 1);
            _avatarGlow.BlurRadius = 20 + 40 * amount;
            _avatarGlow.Opacity = 0.6 + 0.4 * amount;
        }

        private void StopAvatarGlow()
        {
            _avatarGlowLoop?.Stop();
            _avatarGlowLoop = null;
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
