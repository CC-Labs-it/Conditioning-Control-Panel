using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Deeper
{
    /// <summary>
    /// One-click play from Play > Deeper (tier 2 testers, 2026-11-16: "I'd like a 1 click
    /// load/maximize/play from that window"). The caller loads the file as the library row's
    /// play button does; this maximises the window and presses Play for the user once the
    /// media is actually there. Video arrives asynchronously (WebView2 init, navigation, the
    /// site building its &lt;video&gt;), so it waits on a short poll instead of guessing.
    /// </summary>
    public partial class EnhancementPlayerWindow
    {
        private const int PlayNowPollMs = 400;
        private static readonly TimeSpan PlayNowTimeout = TimeSpan.FromSeconds(30);

        private DispatcherTimer? _playNowTimer;
        private string? _playNowPath;
        private DateTime _playNowDeadline;
        private bool _playNowAudioStarted;

        /// <summary>
        /// Maximise this window and start playback of <paramref name="ccpenhJsonPath"/> as soon as
        /// it is loaded. Gives up quietly after 30 s (the Play button still works by hand).
        /// </summary>
        public void MaximizeAndPlayWhenReady(string ccpenhJsonPath)
        {
            if (string.IsNullOrWhiteSpace(ccpenhJsonPath) || _isClosing) return;
            try { WindowState = WindowState.Maximized; }
            catch (Exception ex) { Diag.Swallowed(ex); }

            _playNowPath = ccpenhJsonPath;
            _playNowDeadline = DateTime.UtcNow + PlayNowTimeout;
            _playNowAudioStarted = false;
            if (_playNowTimer == null)
            {
                _playNowTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(PlayNowPollMs),
                };
                _playNowTimer.Tick += (_, _) => PlayNowTick();
                Closing += (_, _) => StopPlayNow();
            }
            _playNowTimer.Start();
        }

        private void StopPlayNow()
        {
            _playNowTimer?.Stop();
            _playNowPath = null;
        }

        private static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private void PlayNowTick()
        {
            try
            {
                if (_isClosing || _playNowPath == null || DateTime.UtcNow > _playNowDeadline)
                {
                    StopPlayNow();
                    return;
                }
                // Not our file yet (the load is still deferred, or another load replaced it).
                var enh = _host.LoadedEnhancement;
                if (enh == null || !SamePath(_host.LoadedFilePath, _playNowPath)) return;

                if (string.Equals(enh.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase))
                {
                    // Wait for THIS file's video source, not the one a reused window still holds.
                    var src = _videoSource;
                    if (src == null || !string.Equals(_currentMediaPath, enh.MediaSource, StringComparison.Ordinal)) return;
                    if (src.IsPlaying) { StopPlayNow(); return; }
                    MaybePromptForWebcamBeforePlay();
                    EnsureVideoEngineBound();
                    // Re-sent each tick until the page reports playing: the site may still be
                    // building its <video> element when the first one goes out.
                    src.Play();
                    return;
                }

                // Audio. Playing counts only when it is THIS file's audio: a reused window can
                // still be rolling the previous track, which LoadAudio below replaces.
                bool ours = SamePath(_player.CurrentPath, enh.MediaSource);
                if (_player.IsPlaying && ours) { StopPlayNow(); return; }
                if (_loadInProgress || _playNowAudioStarted) return;
                if (_player.IsPaused && ours)
                {
                    MaybePromptForWebcamBeforePlay();
                    _player.Resume();
                    BtnPlayPause.Content = "⏸";
                    BindEngineIfReady();
                    StopPlayNow();
                    return;
                }
                if (!string.IsNullOrEmpty(enh.MediaSource) && File.Exists(enh.MediaSource))
                {
                    // LoadAudio decodes the waveform, plays and binds the engine itself.
                    _playNowAudioStarted = true;
                    MaybePromptForWebcamBeforePlay();
                    LoadAudio(enh.MediaSource);
                    return;
                }
                // The file's audio is not on this PC: say so instead of spinning.
                TxtStatus.Text = Loc.Get("deeper_player_status_pick_first");
                StopPlayNow();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: play-now tick failed: {Error}", ex.Message);
                StopPlayNow();
            }
        }
    }
}
