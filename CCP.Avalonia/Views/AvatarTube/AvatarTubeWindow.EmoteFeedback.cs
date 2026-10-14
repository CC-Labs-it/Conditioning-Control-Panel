using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>
        /// WPF ShowEmoteFeedback (AvatarTubeWindow.ChatInput.cs:957): the remote-emote bubble, "Sending..."
        /// while pending, then <c>Sent: "&lt;text&gt;"</c> (40 chars + "..."). Never over an AI bubble or a pending
        /// reply; cuts any preset line and its queue; no sound, no chat history; up one second longer than a
        /// preset line.
        /// </summary>
        public void ShowEmoteFeedback(string text, bool isPending)
        {
            try
            {
                RunOnAvatar(() =>
                {
                    if (_isWaitingForAi || _isShowingAiBubble) return;
                    var safe = (text ?? "").Trim();
                    if (safe.Length > 40) safe = safe.Substring(0, 40) + "...";
                    var content = isPending ? "Sending..." : $"Sent: \"{safe}\"";

                    _speechTimer?.Stop();
                    ClearSpeechQueue();
                    _isGiggling = false;
                    ShowSpeech(content, playSound: false, aiGenerated: false, null, false, preset: true);
                    if (_speechTimer != null) _speechTimer.Interval += TimeSpan.FromSeconds(1);
                });
            }
            catch (Exception ex) { Log.Warning(ex, "[Avatar] ShowEmoteFeedback failed"); }
        }
    }
}
