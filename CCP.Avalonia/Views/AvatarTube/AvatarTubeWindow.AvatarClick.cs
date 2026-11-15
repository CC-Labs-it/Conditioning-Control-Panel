// WPF AvatarTubeWindow.ChatInput.cs:48-117 and Speech.cs:839-865.
using System;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DateTime _avatarLastClick = DateTime.MinValue;
        private Action _cancelMutedIndicator = static () => { };

        internal void HandleAvatarClick()
        {
            App.Achievements?.TrackAvatarClick();
            var now = SpeechNow;
            if ((now - _avatarLastClick).TotalMilliseconds < 300)
            {
                if (IsMuted) ShowAvatarMutedIndicator();
                else if (CoreSettings.Current.AiChatEnabled && CoreAi.IsAvailable) OpenChatInput();
            }
            _avatarLastClick = now;
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
