using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    private static readonly SpeakPromptRuntime SpeakRuntime = SpeakPromptRuntime.CreateProduction(() => MainShellWindow.Current);
    internal Func<TriggerEffectAction, IPlaybackTimeSource, SpeakPromptSession>? SpeakSessionFactory;
    private EnhancementEngine? _deeperEngine;
    private PlayerTimeSource? _deeperSource;

    private void TickDeeper()
    {
        if (_closing || _loadedEnhancement == null || (_deeperEngine == null && !_isPlaying)) return;
        if (_deeperEngine == null)
        {
            _deeperSource = new PlayerTimeSource(this);
            var dispatcher = new SpeakActionDispatcher(SpeakSessionFactory ?? SpeakRuntime.CreateSession);
            // Other effects are not dispatched on this head. Exclude them from the engine too:
            // its stop cleanup must not silence haptics belonging to another feature.
            var speakOnly = new Enhancement
            {
                Metadata = _loadedEnhancement.Metadata, MediaType = _loadedEnhancement.MediaType,
                MediaSource = _loadedEnhancement.MediaSource, Regions = _loadedEnhancement.Regions,
                TimelineItems = _loadedEnhancement.TimelineItems.Where(item => item != null &&
                    (item.Kind == TimelineItemKind.Rule || item.Kind == TimelineItemKind.Effect && item.EffectType == EffectTypes.Speak))
                    .Select(item => item.Kind == TimelineItemKind.Rule && item.Action is not TriggerEffectAction { EffectType: EffectTypes.Speak }
                        ? new TimelineItem { Id = item.Id, Kind = item.Kind, Start = item.Start, Duration = item.Duration, Enabled = false } : item).ToList(),
                Rules = _loadedEnhancement.Rules.Where(rule => rule != null &&
                    rule.Action is TriggerEffectAction { EffectType: EffectTypes.Speak }).ToList()
            };
            _deeperEngine = new EnhancementEngine(speakOnly, _deeperSource, dispatcher);
            _deeperEngine.Start();
        }
        _deeperSource?.Tick(_currentSec);
    }

    private void StopDeeper()
    {
        // Invalidate before cancellation: a held prompt must not resume playback after panic or replacement.
        _deeperSource?.Invalidate();
        _deeperEngine?.Stop();
        _deeperEngine?.Dispose();
        (_deeperEngine, _deeperSource) = (null, null);
    }

    private sealed class PlayerTimeSource : IPlaybackTimeSource
    {
        private readonly EnhancementPlayerWindow _player;
        private bool _valid = true;
        private double _lastTick = double.NaN;
        internal PlayerTimeSource(EnhancementPlayerWindow player) => _player = player;
        public event Action<double>? PlaybackTimeChanged;
        internal void Tick(double t) { if (_player._isPlaying || t != _lastTick) { _lastTick = t; PlaybackTimeChanged?.Invoke(t); } }
        internal void Invalidate() => _valid = false;
        public double GetCurrentTimeSeconds() => _player._currentSec;
        public double GetDurationSeconds() => _player._durationSec;
        public bool IsPlaying => _valid && _player._isPlaying;
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
        public void Seek(double seconds)
        {
            if (!_valid) return;
            _player._currentSec = seconds;
            if (_player._localAudio != null) _player._localAudio.PositionSeconds = seconds;
            if (_player._isVideoMode && _player._videoNavigated)
                _ = _player._videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.currentTime=" + seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ";"));
        }
        public void Pause() => SetPlaying(false);
        public void Play() => SetPlaying(true);
        private void SetPlaying(bool playing)
        {
            if (!_valid || _player._isPlaying == playing) return;
            if (_player._localAudio is { } audio) { if (playing) audio.Play(); else audio.Pause(); }
            else if (_player._isVideoMode && _player._videoNavigated)
                _ = _player._videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke(playing ? "l.play();" : "l.pause();"));
            else return;
            _player._isPlaying = playing;
            _player._txtPlayPauseGlyph.Kind = playing ? IconKind.Pause : IconKind.Play;
            _player._txtStatus.Text = ConditioningControlPanel.Localization.Loc.Get(playing ? "deeper_player_status_playing" : "deeper_player_status_stopped");
            _player.UpdateStatusPill();
        }
    }
}
