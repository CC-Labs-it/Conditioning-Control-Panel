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
    internal Func<IActionDispatcher>? PlayerDispatcherFactory;
    private EnhancementEngine? _deeperEngine;
    private PlayerTimeSource? _deeperSource;

    private void TickDeeper()
    {
        if (_closing || _loadedEnhancement == null || (_deeperEngine == null && !_isPlaying)) return;
        if (_deeperEngine == null)
        {
            _deeperSource = new PlayerTimeSource(this);
            var dispatcher = new SpeakActionDispatcher(SpeakSessionFactory ?? SpeakRuntime.CreateSession,
                PlayerDispatcherFactory?.Invoke() ?? new BrowserEnhancementDispatcher());
            var recorder = new RecordingActionDispatcher(dispatcher);
            recorder.ActionLogged += line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!_closing) IngestActionLine(line);
            });
            // Preserve rule regions while excluding overlays and haptics until cleanup can isolate their layer.
            static bool Unsupported(EnhancementAction? action) => action is TriggerHapticAction or TriggerEffectAction { EffectType: EffectTypes.Overlay or EffectTypes.Haptic };
            var supported = new Enhancement
            {
                Metadata = _loadedEnhancement.Metadata, MediaType = _loadedEnhancement.MediaType,
                MediaSource = _loadedEnhancement.MediaSource, Regions = _loadedEnhancement.Regions,
                TimelineItems = _loadedEnhancement.TimelineItems.Where(item => item != null &&
                    !(item.Kind == TimelineItemKind.Effect && item.EffectType is EffectTypes.Overlay or EffectTypes.Haptic))
                    .Select(item => item.Kind == TimelineItemKind.Rule && Unsupported(item.Action)
                        ? new TimelineItem { Id = item.Id, Kind = item.Kind, Start = item.Start, Duration = item.Duration, Enabled = false } : item).ToList(),
                Rules = _loadedEnhancement.Rules.Where(rule => rule != null && !Unsupported(rule.Action)).ToList()
            };
            _deeperEngine = new EnhancementEngine(supported, _deeperSource, recorder,
                EnhancementHostService.WebcamProvider?.Invoke(), line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_closing) IngestDiagnosticLine(line);
                }));
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
