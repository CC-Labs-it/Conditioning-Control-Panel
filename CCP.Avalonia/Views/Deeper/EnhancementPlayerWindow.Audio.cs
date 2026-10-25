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

    // WPF LoadAudio: replace the previous clip, then play as soon as loading succeeds.
    // ponytail: EnhancementHostService.Bind (rules firing on this clock) needs the engine in Core
    // (deeper-core-c, not under this lane); named in the parity row as -b-next.
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
        // A reused player must not leave a hidden video audible behind its audio pane.
        if (_videoNavigated)
        {
            try { await _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.pause();")); }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: previous video could not pause");
                if (!_isClosing && generation == _audioGeneration)
                    _txtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
                return;
            }
        }
        if (_isClosing || generation != _audioGeneration) return;
        _videoNavigated = false;
        ShowMediaPaneFor(MediaTypes.Audio);
        _peaks = null;
        _waveformPath.Data = null;
        // WPF awaited the decode before Play; a LibVLC transcode of a long file is not instant,
        // so here the clip starts at once and the waveform draws when its peaks land.
        _ = LoadWaveformAsync(path);
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
            _txtStatus.Text = Loc.Get("deeper_player_status_stopped");
        });
        _durationSec = audio.DurationSeconds;
        MaybePromptForWebcamBeforePlay();
        audio.Play();
        _isPlaying = true;
        _txtPlayPauseGlyph.Kind = IconKind.Pause;
        _txtStatus.Text = Loc.Get("deeper_player_status_playing");
        _uiTimer?.Start();
        UiTimer_Tick(null, EventArgs.Empty);
    }

    // WPF LoadWaveformAsync: disk-cached peaks; a decode failure leaves the strip flat.
    private async Task LoadWaveformAsync(string path)
    {
        try
        {
            var data = await AudioWaveformCache.LoadAsync(path);
            if (_isClosing || _lastAudioPath != path) return;
            _peaks = data.Peaks;
            RenderWaveform();
        }
        catch (Exception ex)
        {
            Log.Debug("EnhancementPlayer: waveform decode failed: {Error}", ex.Message);
            if (_isClosing || _lastAudioPath != path) return;
            _peaks = null;
            _waveformPath.Data = null;
        }
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
            window.StopPlayerWebcam();
            if (!window._isVideoMode) window.BtnStop_Click();
            else window._audioGeneration++; // an audio load may still be awaiting video teardown
        }
    }
}
