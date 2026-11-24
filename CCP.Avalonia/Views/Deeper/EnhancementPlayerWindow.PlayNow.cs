using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    private DispatcherTimer? _playNowTimer;
    private string? _playNowPath;
    private DateTime _playNowDeadline;
    private bool _playNowAudioStarted, _playNowVideoPending;
    private int _playNowGeneration;

    // WPF EnhancementPlayerWindow.PlayNow.cs:32: the caller loads first, then requests play.
    public void MaximizeAndPlayWhenReady(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _closing) return;
        WindowState = WindowState.Maximized;
        _playNowPath = path;
        _playNowDeadline = DateTime.UtcNow.AddSeconds(30);
        _playNowAudioStarted = _playNowVideoPending = false;
        ++_playNowGeneration;
        if (_playNowTimer == null)
        {
            _playNowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _playNowTimer.Tick += (_, _) => PlayNowTick();
        }
        _playNowTimer.Start();
    }

    private void StopPlayNow()
    {
        _playNowTimer?.Stop();
        _playNowPath = null;
        ++_playNowGeneration;
    }

    private static bool PlayNowSamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    internal async void PlayNowTick()
    {
        if (_closing || _playNowPath == null || DateTime.UtcNow > _playNowDeadline) { StopPlayNow(); return; }
        var enh = _loadedEnhancement;
        if (enh == null || !PlayNowSamePath(LoadedFilePath, _playNowPath)) return;
        try
        {
            if (string.Equals(enh.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase))
            {
                if (!_videoNavigated || !_isVideoMode || _playNowVideoPending) return;
                if (_isPlaying) { StopPlayNow(); return; }
                var generation = _playNowGeneration;
                var load = _videoLoad;
                _playNowVideoPending = true;
                OfferWebcamBeforePlay();
                TickDeeper(bindVideo: true);
                var result = await _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.play();"));
                if (generation != _playNowGeneration) return;
                _playNowVideoPending = false;
                if (_closing || load != _videoLoad || !_videoNavigated || DeeperPreview.Unquote(result) != "ok") return;
                _isPlaying = true;
                _txtPlayPauseGlyph.Kind = FluentIcons.Common.Symbol.Pause;
                _txtStatus.Text = Loc.Get("deeper_player_status_playing");
                UpdateStatusPill();
                StopPlayNow();
                return;
            }
            var ours = PlayNowSamePath(_audioPath, enh.MediaSource);
            if (ours && _isPlaying) { StopPlayNow(); return; }
            if (_playNowAudioStarted) return;
            if (ours && _localAudio != null)
            {
                OfferWebcamBeforePlay();
                BtnPlayPause_Click();
                StopPlayNow();
                return;
            }
            if (!string.IsNullOrEmpty(enh.MediaSource) && File.Exists(enh.MediaSource))
            {
                _playNowAudioStarted = true;
                _audioPath = enh.MediaSource;
                OfferWebcamBeforePlay();
                LoadAudio(enh.MediaSource);
                return;
            }
            _txtStatus.Text = Loc.Get("deeper_player_status_pick_first");
            StopPlayNow();
        }
        catch (Exception ex) { Log.Debug(ex, "Deeper play-now failed"); StopPlayNow(); }
    }
}
