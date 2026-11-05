// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs:1837-1868 (PanicSilence),
// called from the panic registry ('tube-speech', WPF MainWindow.xaml.cs:1980-1981).

using System;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>Stops the clip PlaySpeechAudio last started (WPF StopSpokenAudio's handle). Never null.</summary>
        private Action _stopSpoken = static () => { };

        // WPF AvatarTube/AvatarTubeWindow.Speech.cs:1911-1915: mute cuts audio, keeps text.
        internal void StopVoiceCommandAudio()
        {
            try { _stopSpoken(); } catch { }
            _stopSpoken = static () => { };
        }

        /// <summary>
        /// WPF PanicSilence: cut the spoken line, drop the thinking/listening animations, the
        /// auto-hide and the bubble in one pass. Nothing is drained afterwards, so nothing speaks
        /// after a panic. Idempotent and safe when idle; every step runs even if one throws.
        /// </summary>
        public void PanicSilence() => RunOnAvatar(() =>
        {
            try { _stopSpoken(); } catch { }
            _stopSpoken = static () => { };
            try { StopThinkingAnimation(); } catch { }
            try { _speechTimer?.Stop(); } catch { }
            try { ClearSpeechQueue(); } catch { }   // WPF Speech.cs:1850-1851: nothing queued speaks after
            try { _listeningDotsTimer?.Stop(); _listeningDotsTimer = null; } catch { }
            _isWaitingForAi = false;
            _isGiggling = false;
            _isShowingAiBubble = false;
            _isListeningBubble = false;
            try { _speechBubble.IsVisible = false; } catch { }   // WPF CollapseSpeechBubble (Speech.cs:2962)
            _lastSpeechEndTime = SpeechNow;
            _isMouseOverSpeechBubble = false;
        });
    }
}
