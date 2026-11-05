using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    private static readonly HashSet<EnhancementPlayerWindow> AudioWindows = new();
    private IDeeperLocalAudio? _localAudio;
    private string? _lastAudioPath;
    private int _audioGeneration;
    private bool _isClosing;

    // WPF EnhancementPlayerWindow.xaml.cs:616-660: replace, load and autoplay.
    // Rule binding is a separate parity gap; this slice supplies transport only.
    private async void LoadAudio(string path)
    {
        try { await LoadAudioAsync(path); }
        catch (Exception ex)
        {
            Log.Warning(ex, "EnhancementPlayer: local audio load failed");
            if (!_isClosing)
            {
                DisposeLocalAudio();
                _txtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
            }
        }
    }

    private async Task LoadAudioAsync(string path)
    {
        if (_isClosing) return;
        DisposeLocalAudio();
        var generation = _audioGeneration;
        _lastAudioPath = path;
        _txtAudioPath.Text = path;
        _txtStatus.Text = Loc.Get("deeper_player_status_loading_audio");
        // WPF EnhancementPlayerWindow.xaml.cs:1325-1353: unload even a pending video.
        // A script failure is returned as null; clearing the page must not depend on it.
        _videoNavigated = false;
        if (_videoBrowser.Source != null)
        {
            var blank = new Uri("about:blank");
            _videoBrowser.AllowNavigation = u => u == blank;
            _videoBrowser.Source = null;
            _videoBrowser.Navigate(blank);
        }
        ShowMediaPaneFor(MediaTypes.Audio);
        _peaks = null;
        _waveformPath.Data = null;
        _currentSec = _durationSec = 0;
        _txtPlayPauseGlyph.Kind = IconKind.Play;
        _txtCurrent.Text = _txtTotal.Text = "0:00";
        IDeeperLocalAudio? audio = null;
        try { audio = await DeeperLocalAudio.Open(path); }
        catch (Exception ex) { Log.Warning(ex, "EnhancementPlayer: audio parse failed"); }
        // Stop, panic, close or a newer pick while the parse was pending owns this result now.
        if (_isClosing || generation != _audioGeneration)
        {
            if (audio != null) { audio.Pause(); audio.Dispose(); }
            return;
        }
        if (audio == null)
        {
            _txtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
            _txtTotal.Text = "0:00";
            UpdateStatusPill();
            return;
        }
        _localAudio = audio;
        audio.Volume = (int)Math.Round(_sliderVolume.Value);
        audio.Ended += () => Dispatcher.UIThread.Post(() =>
        {
            if (_isClosing || _localAudio != audio) return;
            _isPlaying = false;
            _currentSec = 0;
            _txtPlayPauseGlyph.Kind = IconKind.Play;
            UiTimer_Tick(null, EventArgs.Empty);
            _txtStatus.Text = Loc.Get("deeper_player_status_ended");
        });
        audio.Failed += () => Dispatcher.UIThread.Post(() =>
        {
            if (!_isClosing && _localAudio == audio) FailLocalAudio();
        });
        _durationSec = audio.DurationSeconds;
        if (!audio.Play()) { FailLocalAudio(); return; }
        _isPlaying = true;
        _txtPlayPauseGlyph.Kind = IconKind.Pause;
        _txtStatus.Text = Loc.Get("deeper_player_status_playing");
        _uiTimer?.Start();
        UiTimer_Tick(null, EventArgs.Empty);
    }

    // WPF EnhancementPlayerWindow.xaml.cs:648-652; EnhancementAudioPlayer.cs:178-205 errors.
    private void FailLocalAudio()
    {
        DisposeLocalAudio();
        _currentSec = _durationSec = 0;
        _txtCurrent.Text = _txtTotal.Text = "0:00";
        _txtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
        var generation = _audioGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isClosing && generation == _audioGeneration)
                _txtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
        }); // Discovery runs after a synchronous picker load; retain the playback error.
    }

    private void DisposeLocalAudio()
    {
        _audioGeneration++;
        var audio = _localAudio;
        _localAudio = null;
        _isPlaying = false;
        _txtPlayPauseGlyph.Kind = IconKind.Play;
        UpdateStatusPill();
        if (audio != null) { audio.Pause(); audio.Dispose(); }
    }

    // WPF EnhancementPlayerWindow.xaml.cs:1173-1180 and Mission3.cs:772-789 mini-timeline seek.
    private void SeekAudio(double seconds)
    {
        if (_localAudio == null) return;
        _localAudio.PositionSeconds = seconds;
        _currentSec = _localAudio.PositionSeconds;
        UiTimer_Tick(null, EventArgs.Empty);
    }

    internal static void StopAllAudioForPanic()
    {
        // Pending loads are included: stopping only an already-playing clip races its parse.
        foreach (var window in AudioWindows.ToArray())
        {
            if (!window._isVideoMode) window.BtnStop_Click();
            else window._audioGeneration++; // defensive invalidation while the video pane is active
        }
    }
}
